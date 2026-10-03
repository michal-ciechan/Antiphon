using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The model, active revision and resolution inputs authorized before a claim.</summary>
internal sealed record CodexCliProbeDescriptor(
    string RunnerId, string? Model, Guid? ProfileRevisionId, RunnerCodexCliProbeRequest? Request,
    string? Error = null)
{
    internal static async Task<CodexCliProbeDescriptor?> ResolveAsync(
        AppDbContext db, AgentTask task, AgentRegistrySettings? registry, PhoneHomeLaunchPolicy? phoneHome,
        ApiKeyEnvResolver? keys, CancellationToken ct)
    {
        if (task.AgentKind != AgentKind.Codex) return null;
        var runner = string.IsNullOrWhiteSpace(task.RunnerId) ? "desktop" : task.RunnerId;
        var agent = task.AgentId is Guid id
            ? await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) : null;
        var model = task.SpecialistModelAlias ?? (string.IsNullOrWhiteSpace(agent?.ModelId)
            ? ModelLevelAliases.ForCodex(task.ModelLevel) : agent.ModelId.Trim());
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? executable = null;
        IReadOnlyList<string> arguments = [];
        Guid? revisionId = null;
        if (agent is { IsPoolDelegate: false, TuiProfileId: Guid profileId })
        {
            var profile = await db.AgentTuiProfiles.AsNoTracking().Include(p => p.ActiveRevision)
                .SingleOrDefaultAsync(p => p.Id == profileId, ct)
                ?? throw new NotFoundException(nameof(AgentTuiProfile), profileId);
            if (!profile.IsEnabled) throw new ConflictException("The selected runner profile is disabled.", "profile_disabled");
            var revision = profile.ActiveRevision
                ?? throw new ConflictException("The selected runner profile has no active revision.", "profile_not_validated");
            if (profile.Kind != task.AgentKind)
                throw new ConflictException("The pinned agent's current profile kind changed; recreate the task.");
            if (string.IsNullOrWhiteSpace(revision.ModelArgumentName))
            {
                if (!string.IsNullOrWhiteSpace(agent.ModelId))
                    throw new ConflictException("The selected runner profile passes no model argument; clear the exact model or set the profile's model argument name.", "model_argument_unsupported");
                model = null;
            }
            revisionId = revision.Id;
            executable = AgentExecutableResolver.Default.TryResolve(revision.Executable) ?? revision.Executable;
            arguments = JsonSerializer.Deserialize<string[]>(revision.ArgumentsJson) ?? [];
            Overlay(env, JsonSerializer.Deserialize<Dictionary<string,string>>(revision.NonSecretEnvironmentJson));
            var secretNames = JsonSerializer.Deserialize<string[]>(revision.SecretEnvironmentNamesJson) ?? [];
            if (secretNames.Any(IsResolutionName))
                return new(runner, model, revisionId, null, "launcher_unverified");
        }
        else if (registry is not null)
        {
            // Same preferred-definition and ordered-kind lookup as AgentRegistry.DefinitionNameForKind.
            var preferred = registry.Definitions.GetValueOrDefault(registry.DefaultDefinition);
            var definition = preferred is not null && Enum.TryParse<AgentKind>(preferred.Kind, true, out var kind) && kind == task.AgentKind
                ? preferred : registry.Definitions.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(d => Enum.TryParse<AgentKind>(d.Value.Kind, true, out var k) && k == task.AgentKind).Value;
            if (definition is not null)
            {
                executable = AgentExecutableResolver.Default.TryResolve(definition.Exe) ?? definition.Exe;
                arguments = definition.ArgsTemplate;
                Overlay(env, definition.Env);
                if (definition.SecretEnvironmentNames.Any(IsResolutionName))
                    return new(runner, model, null, null, "launcher_unverified");
            }
        }

        // An already-running pinned process uses the model frozen on its accepted session.
        if (agent is not null && Guid.TryParse(agent.PersistentSessionId, out var sessionId))
        {
            var live = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            if (live is { Status: SessionStatus.Running, EffectiveModelId: { Length: > 0 } actual }) model = actual;
        }
        if (ModelLevelAliases.MinimumCodexCliVersion(task.AgentKind, model) is null)
            return new(runner, model, revisionId, null);
        if (executable is null) return new(runner, model, revisionId, null, "launcher_unverified");

        if (keys is not null)
        {
            var projectId = task.ProjectId ?? await keys.ResolveProjectIdAsync(agent?.BoardId, ct);
            Overlay(env, await keys.GetProjectDefaultEnvAsync(projectId, ct));
        }
        Overlay(env, AgentLaunchEnv.Parse(task.InheritedLaunchEnvJson));
        Overlay(env, AgentLaunchEnv.ParseForAgent(agent));
        Overlay(env, AgentLaunchEnv.Parse(task.LaunchEnvOverrideJson));
        if (env.Any(e => IsLoaderName(e.Key) && !string.IsNullOrEmpty(e.Value)))
            return new(runner, model, revisionId, null, "launcher_unverified");
        var cwd = task.WorktreePath ?? task.WorkingDirectory;
        if (phoneHome?.IsRunnerBound(task.RunnerId) == true)
        {
            var projected = phoneHome.Project(new("codex", AgentKind.Codex, executable, arguments,
                env, cwd, 120, 30), agent ?? new Agent { RunnerId = task.RunnerId },
                task.RemoteWorktreePath ?? phoneHome.RunnerWorkspaceFor(task.RunnerId));
            executable = projected.Exe;
            cwd = projected.Cwd;
        }
        return FromSpec(runner, model, revisionId, executable, arguments, env, cwd);
    }

    internal static CodexCliProbeDescriptor FromSpec(string runner, string? model, Guid? revision,
        string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string,string> env, string cwd)
    {
        var node = Path.GetFileName(executable.Replace('\\','/'));
        var prefix = node.Equals("node", StringComparison.OrdinalIgnoreCase) || node.Equals("node.exe", StringComparison.OrdinalIgnoreCase)
            ? args.FirstOrDefault() : null;
        string? Get(string name) => env.FirstOrDefault(e => e.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        if (env.Any(e => IsLoaderName(e.Key) && !string.IsNullOrEmpty(e.Value)))
            return new(runner, model, revision, null, "launcher_unverified");
        var request = new RunnerCodexCliProbeRequest(executable, cwd, Get("PATH"), Get("PATHEXT"), prefix);
        if (new[] { request.Executable, request.ResolutionCwd, request.Path, request.PathExt, request.CodexJsPrefix }
            .Any(v => v is not null && (v.Length > 32768 || v.Contains('\0') || v.Contains("${secret:", StringComparison.OrdinalIgnoreCase)
                || v.Contains("{{key:", StringComparison.OrdinalIgnoreCase))))
            return new(runner, model, revision, null, "launcher_unverified");
        return new(runner, model, revision, request);
    }

    private static bool IsLoaderName(string name) => name.Equals("NODE_OPTIONS", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NODE_PATH", StringComparison.OrdinalIgnoreCase) || name.Equals("LD_PRELOAD", StringComparison.OrdinalIgnoreCase)
        || name.Equals("LD_LIBRARY_PATH", StringComparison.OrdinalIgnoreCase) || name.Equals("DOTNET_STARTUP_HOOKS", StringComparison.OrdinalIgnoreCase);
    private static bool IsResolutionName(string name) => name.Equals("PATH", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) || IsLoaderName(name);
    private static void Overlay(IDictionary<string,string> target, IReadOnlyDictionary<string,string>? values)
    { if (values is not null) foreach (var (name,value) in values) if (IsResolutionName(name)) target[name] = value; }
}

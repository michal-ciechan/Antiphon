using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed record ResolvedAgentTuiLaunch(
    AgentLaunchSpec Spec,
    Guid? ProfileId,
    Guid? ProfileRevisionId,
    string? EffectiveModelId,
    AgentTuiLaunchActivityMode ActivityMode);

/// <summary>
/// Selects the managed-profile resolver when it is available, retaining the configured-file
/// registry as the migration and rollback path when no managed installation default exists.
/// </summary>
internal static class AgentLaunchResolution
{
    public static async Task<ResolvedAgentTuiLaunch> ResolveForAgentAsync(
        Agent agent,
        AgentRegistry agentRegistry,
        AgentTuiLaunchResolver? launchResolver,
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        if (launchResolver is null)
        {
            if (agent.TuiProfileId is not null)
            {
                throw new ConflictException(
                    "The selected runner profile cannot be resolved by this installation.",
                    "profile_resolution_unavailable");
            }

            return ResolveLegacy(agentRegistry, options);
        }

        try
        {
            return await launchResolver.ResolveForAgentAsync(agent, options, cancellationToken);
        }
        catch (ConflictException exception)
            when (agent.TuiProfileId is null && exception.Code == "profile_not_found")
        {
            return ResolveLegacy(agentRegistry, options);
        }
    }

    public static async Task<ResolvedAgentTuiLaunch> ResolveDefaultAsync(
        AgentRegistry agentRegistry,
        AgentTuiLaunchResolver? launchResolver,
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        if (launchResolver is null)
            return ResolveLegacy(agentRegistry, options);

        try
        {
            return await launchResolver.ResolveDefaultAsync(options, cancellationToken);
        }
        catch (ConflictException exception) when (exception.Code == "profile_not_found")
        {
            return ResolveLegacy(agentRegistry, options);
        }
    }

    private static ResolvedAgentTuiLaunch ResolveLegacy(
        AgentRegistry agentRegistry,
        AgentLaunchOptions options)
    {
        var spec = agentRegistry.Resolve(agentRegistry.Settings.DefaultDefinition, options);
        return new ResolvedAgentTuiLaunch(
            spec,
            ProfileId: null,
            ProfileRevisionId: null,
            EffectiveModelId: null,
            ActivityMode: AgentTuiLaunchActivityMode.Unknown);
    }
}

public sealed class AgentTuiLaunchResolver
{
    private static readonly string[] ClaudeNestingMarkers =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_BRIDGE_SESSION_ID",
        "CLAUDE_CODE_ENTRYPOINT",
    ];

    private const int MaximumEnvironmentValueLength = 4000;
    private readonly AppDbContext _db;
    private readonly IAgentTuiSecretProtector _secretProtector;
    private readonly AgentTuiMetrics _metrics;
    private readonly AgentTuiRunnerCatalog _runnerCatalog;
    private readonly IEqualityComparer<string> _environmentNameComparer;

    public AgentTuiLaunchResolver(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AgentTuiMetrics metrics,
        AgentTuiRunnerCatalog runnerCatalog)
        : this(
            db,
            secretProtector,
            metrics,
            runnerCatalog,
            AgentEnvironmentVariableNames.ForCurrentPlatform())
    {
    }

    internal AgentTuiLaunchResolver(
        AppDbContext db,
        IAgentTuiSecretProtector secretProtector,
        AgentTuiMetrics metrics,
        AgentTuiRunnerCatalog runnerCatalog,
        IEqualityComparer<string> environmentNameComparer)
    {
        _db = db;
        _secretProtector = secretProtector;
        _metrics = metrics;
        _runnerCatalog = runnerCatalog;
        _environmentNameComparer = environmentNameComparer;
    }

    public async Task<ResolvedAgentTuiLaunch> ResolveForAgentAsync(
        Agent agent,
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        AgentKind? runnerType = null;
        try
        {
            var resolved = await ResolveCoreAsync(agent, options, cancellationToken);
            runnerType = resolved.Spec.Kind;
            _metrics.RecordLaunch(
                resolved.Spec.Kind,
                AgentTuiMetricOutcome.Succeeded,
                string.IsNullOrWhiteSpace(resolved.EffectiveModelId)
                    ? AgentTuiLaunchModelMode.Default
                    : AgentTuiLaunchModelMode.Exact,
                resolved.ActivityMode,
                Stopwatch.GetElapsedTime(startedAt));
            return resolved;
        }
        catch (Exception)
        {
            if (runnerType is { } kind)
            {
                _metrics.RecordLaunch(
                    kind,
                    AgentTuiMetricOutcome.Failed,
                    string.IsNullOrWhiteSpace(agent.ModelId)
                        ? AgentTuiLaunchModelMode.Default
                        : AgentTuiLaunchModelMode.Exact,
                    ActivityModeFor(kind),
                    Stopwatch.GetElapsedTime(startedAt));
            }
            throw;
        }
    }

    public async Task<ResolvedAgentTuiLaunch> ResolveDefaultAsync(
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        var agent = new Agent
        {
            Id = Guid.Empty,
            Name = "default",
            TuiProfileId = null,
            ModelId = null
        };
        return await ResolveForAgentAsync(agent, options, cancellationToken);
    }

    private async Task<ResolvedAgentTuiLaunch> ResolveCoreAsync(
        Agent agent,
        AgentLaunchOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Cols <= 0)
            throw new ArgumentException("Cols must be positive.", nameof(options));
        if (options.Rows <= 0)
            throw new ArgumentException("Rows must be positive.", nameof(options));

        var profile = await LoadProfileAsync(agent.TuiProfileId, cancellationToken);
        if (!profile.IsEnabled)
        {
            throw new ConflictException(
                "The selected runner profile is disabled.",
                "profile_disabled");
        }

        var revision = profile.ActiveRevision
            ?? throw new ConflictException(
                "The selected runner profile has no active revision.",
                "profile_not_validated");

        var arguments = DeserializeArray(revision.ArgumentsJson);
        var nonSecretEnvironment = DeserializeDictionary(revision.NonSecretEnvironmentJson);
        var secretNames = DeserializeArray(revision.SecretEnvironmentNamesJson);
        var environment = new Dictionary<string, string>(
            nonSecretEnvironment,
            StringComparer.Ordinal);

        if (revision.AuthenticationMode == AgentTuiAuthenticationMode.ManagedEnvironment)
        {
            foreach (var declaredName in secretNames)
            {
                var matches = profile.Secrets
                    .Where(secret => _environmentNameComparer.Equals(secret.Name, declaredName))
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new ConflictException(
                        "One or more managed credentials are missing for the selected profile.",
                        "profile_not_validated");
                }

                try
                {
                    var plaintext = _secretProtector.Unprotect(
                        profile.Id,
                        matches[0].Name,
                        matches[0].Ciphertext);
                    if (string.IsNullOrEmpty(plaintext) || plaintext.Length > MaximumEnvironmentValueLength)
                    {
                        throw new ConflictException(
                            "A managed credential could not be read safely.",
                            "secret_protection_unavailable");
                    }
                    environment[matches[0].Name] = plaintext;
                }
                catch (CryptographicException exception)
                {
                    throw new ServiceUnavailableException(
                        "Managed-secret protection is unavailable for launch.",
                        "secret_protection_unavailable",
                        exception);
                }
            }
        }

        if (options.ExtraEnv is not null)
        {
            foreach (var (key, value) in options.ExtraEnv)
                environment[key] = value;
        }

        var args = new List<string>(arguments);
        if (options.ExtraArgs is not null)
            args.AddRange(options.ExtraArgs);

        string? effectiveModelId = null;
        if (!string.IsNullOrWhiteSpace(agent.ModelId))
        {
            effectiveModelId = agent.ModelId.Trim();
            EnsureModelAllowed(profile, effectiveModelId);
            var modelArgumentName = string.IsNullOrWhiteSpace(revision.ModelArgumentName)
                ? "--model"
                : revision.ModelArgumentName.Trim();
            args.Add(modelArgumentName);
            args.Add(effectiveModelId);
        }

        ApplyClaudeEnvironmentDefaults(profile.Kind, environment);
        ApplyGrokEnvironmentDefaults(profile.Kind, environment);

        var cwd = string.IsNullOrWhiteSpace(options.Cwd)
            ? string.IsNullOrWhiteSpace(revision.WorkingDirectory)
                ? Environment.CurrentDirectory
                : revision.WorkingDirectory
            : options.Cwd;
        if (!string.IsNullOrWhiteSpace(cwd))
            cwd = Path.GetFullPath(cwd);

        var exe = AgentExecutableResolver.Default.TryResolve(revision.Executable)
                   ?? revision.Executable;
        var definitionName = profile.SourceDefinitionName
                             ?? profile.DisplayName;

        var spec = new AgentLaunchSpec(
            DefinitionName: definitionName,
            Kind: profile.Kind,
            Exe: exe,
            Args: args.AsReadOnly(),
            Env: environment,
            Cwd: cwd,
            Cols: options.Cols,
            Rows: options.Rows);

        return new ResolvedAgentTuiLaunch(
            spec,
            profile.Id,
            revision.Id,
            effectiveModelId,
            ActivityModeFor(profile.Kind));
    }

    private async Task<AgentTuiProfile> LoadProfileAsync(
        Guid? profileId,
        CancellationToken cancellationToken)
    {
        IQueryable<AgentTuiProfile> query = _db.AgentTuiProfiles
            .AsNoTracking()
            .Include(profile => profile.ActiveRevision)
            .Include(profile => profile.Secrets)
            .Include(profile => profile.Models)
            .AsSplitQuery();

        AgentTuiProfile? profile;
        if (profileId is { } selectedId)
        {
            profile = await query.SingleOrDefaultAsync(
                candidate => candidate.Id == selectedId,
                cancellationToken);
            if (profile is null)
            {
                throw new NotFoundException(nameof(AgentTuiProfile), selectedId);
            }
        }
        else
        {
            profile = await query.SingleOrDefaultAsync(
                candidate => candidate.IsDefault,
                cancellationToken);
            if (profile is null)
            {
                throw new ConflictException(
                    "No installation default runner profile is configured.",
                    "profile_not_found");
            }
        }

        return profile;
    }

    private void EnsureModelAllowed(AgentTuiProfile profile, string modelId)
    {
        if (profile.Models.Any(model =>
                string.Equals(model.Identifier, modelId, StringComparison.Ordinal)))
        {
            return;
        }

        if (_runnerCatalog.Get(profile.Kind).CuratedModels.Any(model =>
                string.Equals(model.Identifier, modelId, StringComparison.Ordinal)))
        {
            return;
        }

        throw new ConflictException(
            "The selected model is not part of the profile catalogue.",
            "model_not_in_profile");
    }

    private static void ApplyClaudeEnvironmentDefaults(
        AgentKind kind,
        IDictionary<string, string> environment)
    {
        if (kind != AgentKind.ClaudeCode)
            return;
        if (!environment.ContainsKey("DISABLE_AUTOUPDATER"))
            environment["DISABLE_AUTOUPDATER"] = "1";
        if (!environment.ContainsKey("CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"))
            environment["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1";
        foreach (var marker in ClaudeNestingMarkers)
        {
            if (!environment.ContainsKey(marker))
                environment[marker] = string.Empty;
        }
    }

    private static void ApplyGrokEnvironmentDefaults(
        AgentKind kind,
        IDictionary<string, string> environment)
    {
        if (kind != AgentKind.Grok)
            return;
        if (!environment.ContainsKey("GROK_TELEMETRY_ENABLED"))
            environment["GROK_TELEMETRY_ENABLED"] = "0";
        if (!environment.ContainsKey("GROK_FEEDBACK_ENABLED"))
            environment["GROK_FEEDBACK_ENABLED"] = "0";
    }

    private static AgentTuiLaunchActivityMode ActivityModeFor(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => AgentTuiLaunchActivityMode.Structured,
        AgentKind.Codex or AgentKind.OpenCode or AgentKind.Grok or AgentKind.Raw => AgentTuiLaunchActivityMode.QuietTime,
        _ => AgentTuiLaunchActivityMode.Unknown
    };

    private static string[] DeserializeArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    private static Dictionary<string, string> DeserializeDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

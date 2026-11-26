using Antiphon.TestSupport.LiveClaude;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using TUnit.Core.Exceptions;

namespace Antiphon.LiveClaude.Tests;

// The C680 canary's launch builders. Antiphon.Tests compiles this file too (LiveLaneExecutableTests and
// LiveLaneIsolationTests call the builders with owned fakes); the test method itself is only in this project.
public partial class ClaudeDialogWatchdogCanaryTests
{
    internal static LiveClaudeExecutable PrepareExecutable(LiveClaudeLaunchRequest? request = null,
        LiveClaudeLaunchContext? context = null, Func<string>? legacy = null)
        => LiveClaudeExecutable.Resolve(LiveClaudeCore.C680, request, context,
            legacy ?? (() => Resolve(OperatingSystem.IsWindows() ? "claude.exe" : "claude")));

    internal static ProcessStartInfo CreateVersionProbe(LiveClaudeExecutable executable)
        => new(executable.RequirePath(), "--version")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

    internal static AgentLaunchSpec CreateLaunchSpec(LiveClaudeExecutable executable, string[] args,
        Dictionary<string, string> env, string cwd, Guid sessionId, DateTime generation)
        => new("c680-native", AgentKind.ClaudeCode, executable.RequirePath(), args, env, cwd, 120, 40,
            SessionId: sessionId, AcceptedStartedAt: generation);

    internal static LiveClaudeProfileSpec BuildProfileLaunch(LiveClaudeProfile profile, string role,
        string session, string? overlay = null)
    {
        var spec = profile.Build(role, session, overlay);
        return spec;
    }
    internal static LiveClaudeSettingsProjection ProjectProfile(LiveClaudeProfile profile, string[] args)
        => profile.Project(args);

    internal static AgentLaunchSpec CreateProfileLaunch(LiveClaudeExecutable executable, LiveClaudeProfile profile,
        string settings, Guid sessionId, DateTime generation)
    {
        var launch = BuildProfileLaunch(profile, "control", sessionId.ToString("D"));
        var args = launch.Args.Where(a => a != "--dangerously-skip-permissions").Concat(new[] {
            "--permission-mode", "default", "--settings", settings, "--strict-mcp-config", "--tools", "Bash,AskUserQuestion" }).ToArray();
        // Deliberate C680 tool restrictions are recorded separately from startup argv parity.
        profile.Capture(args);
        foreach (var name in new[] { "CLAUDECODE", "ANTIPHON_TASK_TOKEN", "ANTIPHON_TASK_ID", "ANTIPHON_API", "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS" })
            launch.Env[name] = "";
        return CreateLaunchSpec(executable, args, launch.Env, launch.Cwd, sessionId, generation);
    }

    private static string Resolve(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.Combine(d, name))
        .FirstOrDefault(File.Exists) ?? throw new SkipTestException($"Native qualification requires {name}.");
}

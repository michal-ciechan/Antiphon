using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Seeds Claude project trust in the config directory the spawned Claude will read.
/// Acts only for a Claude file name. Never throws and never creates the config file.
/// </summary>
internal static class ClaudeLaunchTrustSeed
{
    public static string? ConfigPathFor(
        string? exe,
        IReadOnlyDictionary<string, string>? env,
        IReadOnlyDictionary<string, string?>? processEnv)
    {
        _ = exe;
        _ = env;
        _ = processEnv;
        return null;
    }

    public static ClaudeProjectTrustResult? Apply(
        RunnerLaunchRequest request,
        IReadOnlyDictionary<string, string?>? processEnv,
        Action<string>? log)
    {
        _ = ConfigPathFor(request.Exe, request.Env, processEnv);
        _ = log;
        return null;
    }
}

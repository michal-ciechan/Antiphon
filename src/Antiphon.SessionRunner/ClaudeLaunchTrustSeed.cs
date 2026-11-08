using System.Collections;
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
        try
        {
            if (!IsClaudeFileName(exe))
                return null;
            // The injected process map is the only process source. DefaultConfigPath() would
            // re-read the live environment and ignore a test dictionary.
            var dir = RequestConfigDirectory(env) ?? ProcessConfigDirectory(processEnv);
            if (!string.IsNullOrWhiteSpace(dir))
                return ClaudeProjectTrust.DefaultConfigPath(dir);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude.json");
        }
        catch
        {
            return null;
        }
    }

    public static ClaudeProjectTrustResult? Apply(
        RunnerLaunchRequest request,
        IReadOnlyDictionary<string, string?>? processEnv,
        Action<string>? log)
    {
        try
        {
            if (!IsClaudeFileName(request.Exe))
                return null;
            var path = ConfigPathFor(request.Exe, request.Env, processEnv);
            if (path is null)
                return null;
            var result = ClaudeProjectTrust.Seed(request.Cwd, path);
            WriteLog(log, $"Claude trust seed {result.Outcome} session {request.SessionId:D}");
            return result;
        }
        catch (Exception ex)
        {
            WriteLog(log, $"Claude trust seed {ClaudeProjectTrustOutcome.Failed} session {request.SessionId:D}");
            return new ClaudeProjectTrustResult(
                ClaudeProjectTrustOutcome.Failed, "", request.Cwd, ex.Message);
        }
    }

    internal static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        var source = Environment.GetEnvironmentVariables();
        var map = new Dictionary<string, string?>(source.Count, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is string key)
                map[key] = entry.Value as string;
        }

        return map;
    }

    private static bool IsClaudeFileName(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
            return false;
        var fileName = Path.GetFileName(exe.Replace('\\', '/'));
        return string.Equals(fileName, "claude", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "claude.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "claude.cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RequestConfigDirectory(IReadOnlyDictionary<string, string>? env)
    {
        if (env is null || !env.TryGetValue("CLAUDE_CONFIG_DIR", out var value))
            return null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ProcessConfigDirectory(IReadOnlyDictionary<string, string?>? env)
    {
        if (env is null || !env.TryGetValue("CLAUDE_CONFIG_DIR", out var value))
            return null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void WriteLog(Action<string>? log, string line)
    {
        try
        {
            log?.Invoke(line);
        }
        catch
        {
            // A failing logger must not fail the launch.
        }
    }
}

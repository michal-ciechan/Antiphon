using System.Runtime.InteropServices;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Headed-test eligibility gate. Mirrors ClSession in Antiphon.Agents.Pty.Tests
/// (kept local so Antiphon.Tests does not reference another test project).
/// </summary>
internal static class HeadedClaudeGate
{
    public const string EnvFlag = "ANTIPHON_HEADED_TESTS";

    public static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed-claude tests");
        if (ResolveCl() is null && ResolveClaude() is null)
            throw new SkipTestException("Neither cl.bat/cl.ps1 nor claude found on PATH; cannot run headed tests");
    }

    public static string ResolveOrThrow()
        => ResolveCl() ?? ResolveClaude()
           ?? throw new InvalidOperationException("Neither cl nor claude found on PATH");

    public static string? ResolveCl()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "cl.bat", "cl.ps1", "cl.cmd", "cl.exe" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static string? ResolveClaude()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat", "claude.ps1" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Builds the (app, args[]) tuple for spawning the resolved cl/claude binary via ConPTY.</summary>
    public static (string App, string[] Args) BuildLaunch(string clOrClaude, params string[] extraArgs)
    {
        if (clOrClaude.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", clOrClaude };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }
        if (clOrClaude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return (clOrClaude, extraArgs);
        }
        var cmdArgs = new List<string> { "/d", "/c", clOrClaude };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }
}

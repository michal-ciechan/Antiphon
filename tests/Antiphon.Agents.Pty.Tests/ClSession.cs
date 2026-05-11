using System.Runtime.InteropServices;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

internal static class ClSession
{
    public const string EnvFlag = "ANTIPHON_HEADED_TESTS";

    public static void SkipIfNotEligible()
    {
        if (!(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))) throw new SkipTestException("Headed tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1") throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed-claude tests");
        if (ResolveCl() is null && ResolveClaude() is null) throw new SkipTestException("Neither cl.bat/cl.ps1 nor claude found on PATH; cannot run headed tests");
    }

    /// <summary>
    /// Prefers cl.bat/cl.ps1 wrapper; falls back to the globally-installed claude binary.
    /// </summary>
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

    public static (string app, string[] args) BuildLaunch(string cl, params string[] extraArgs)
    {
        if (cl.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", cl };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }
        if (cl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return (cl, extraArgs);
        }
        // .bat / .cmd — run via cmd.exe
        var cmdArgs = new List<string> { "/d", "/c", cl };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }
}

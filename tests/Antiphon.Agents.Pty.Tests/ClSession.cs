using System.Runtime.InteropServices;
using Xunit;

namespace Antiphon.Agents.Pty.Tests;

internal static class ClSession
{
    public const string EnvFlag = "ANTIPHON_HEADED_TESTS";

    public static void SkipIfNotEligible()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Headed tests require Windows ConPTY");
        Skip.If(Environment.GetEnvironmentVariable(EnvFlag) != "1",
            $"Set {EnvFlag}=1 to opt in to headed-claude tests");
        Skip.If(ResolveCl() is null,
            "cl.bat / cl.ps1 not on PATH; cannot run headed tests");
    }

    public static string ResolveOrThrow()
        => ResolveCl() ?? throw new InvalidOperationException("cl not found on PATH");

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

    public static (string app, string[] args) BuildLaunch(string cl, params string[] extraArgs)
    {
        if (cl.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", cl };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }
        var cmdArgs = new List<string> { "/d", "/c", cl };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }
}

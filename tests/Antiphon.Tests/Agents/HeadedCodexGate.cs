using System.Runtime.InteropServices;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

internal static class HeadedCodexGate
{
    public const string EnvFlag = "ANTIPHON_CODEX_HEADED_TESTS";

    public static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed Codex tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed-codex tests");
        if (ResolveCx() is null)
            throw new SkipTestException("cx.ps1 was not found; cannot run headed Codex tests");
    }

    public static string ResolveOrThrow()
        => ResolveCx() ?? throw new InvalidOperationException("cx.ps1 was not found");

    private static string? ResolveCx()
    {
        var explicitPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "bin",
            "cx.ps1");
        if (File.Exists(explicitPath))
            return explicitPath;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "cx.ps1", "cx.cmd", "cx.bat", "cx.exe" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public static (string App, string[] Args) BuildLaunch(string cx, params string[] extraArgs)
    {
        if (cx.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", cx };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }

        if (cx.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (cx, extraArgs);

        var cmdArgs = new List<string> { "/d", "/c", cx };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }
}

using System.Runtime.InteropServices;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Headed-Codex eligibility gate. Mirrors <c>CxSession</c> in Antiphon.Agents.Pty.Tests (kept local
/// so Antiphon.Tests does not reference another test project), the same way
/// <see cref="HeadedClaudeGate"/> mirrors <c>ClSession</c>.
///
/// <para><b>CARD-0099 S2 — this gate used to look for <c>cx.ps1</c> and find nothing.</b> The
/// <c>Agents:Definitions:codex</c> launch definition names a <c>cx.ps1</c> wrapper that does not
/// exist anywhere on this machine, so <c>ResolveCx()</c> returned null and every headed Codex test
/// skipped silently — which is why nothing about Codex's composer had ever been measured. The user's
/// decision (2026-08-20) is to point at the npm shim rather than restore the wrapper, so that is
/// what this resolves.</para>
/// </summary>
internal static class HeadedCodexGate
{
    public const string EnvFlag = "ANTIPHON_CODEX_HEADED_TESTS";

    /// <summary>Escape hatch: an explicit path wins over every heuristic below.</summary>
    public const string ExeOverrideEnv = "ANTIPHON_CODEX_EXE";

    public static void SkipIfNotEligible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Headed Codex tests require Windows ConPTY");
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed-codex tests");
        if (ResolveCodex() is null)
            throw new SkipTestException(
                $"codex was not found (npm shim, {ExeOverrideEnv}, or PATH); cannot run headed Codex tests");
    }

    public static string ResolveOrThrow()
        => ResolveCodex() ?? throw new InvalidOperationException("codex was not found");

    /// <summary>
    /// Resolution order, most-faithful-and-most-killable first:
    /// <list type="number">
    /// <item><c>ANTIPHON_CODEX_EXE</c> — an explicit path, for a machine that installs codex some
    /// other way.</item>
    /// <item>The <b>native</b> <c>codex.exe</c> the npm package vendors
    /// (<c>%APPDATA%\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-*\vendor\&lt;triple&gt;\bin\codex.exe</c>).
    /// Preferred over the shim because the shim is <c>cmd.exe → node → codex.exe</c>, and
    /// <c>PtyAgentRunner.KillAsync</c> kills the direct child only — killing cmd would orphan the
    /// node and the TUI, which for a test that spends real model turns means live processes left
    /// behind on every run.</item>
    /// <item>The npm <c>codex.cmd</c>/<c>codex.ps1</c> shim in <c>%APPDATA%\npm</c> — the command the
    /// launch definition names, kept as the fallback so a package layout change does not re-break
    /// the gate into silence.</item>
    /// <item>PATH.</item>
    /// </list>
    /// </summary>
    public static string? ResolveCodex()
    {
        var explicitPath = Environment.GetEnvironmentVariable(ExeOverrideEnv);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var npmRoot = NpmGlobalRoot();
        if (npmRoot is not null)
        {
            if (ResolveVendoredCodexExe(npmRoot) is { } vendored)
                return vendored;
            foreach (var name in new[] { "codex.cmd", "codex.ps1", "codex.bat", "codex.exe" })
            {
                var candidate = Path.Combine(npmRoot, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "codex.exe", "codex.cmd", "codex.bat", "codex.ps1" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static string? NpmGlobalRoot()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrWhiteSpace(appData)) return null;
        var root = Path.Combine(appData, "npm");
        return Directory.Exists(root) ? root : null;
    }

    /// <summary>
    /// Globbed rather than hard-coded: the platform sub-package is per-arch
    /// (<c>codex-win32-x64</c>/<c>-arm64</c>) and the vendor directory is keyed by target triple, so
    /// a pinned path would break on an arm64 machine or a package-layout bump — quietly, back into
    /// the skip that hid this subsystem for months.
    /// </summary>
    private static string? ResolveVendoredCodexExe(string npmRoot)
    {
        var pkg = Path.Combine(npmRoot, "node_modules", "@openai", "codex");
        if (!Directory.Exists(pkg)) return null;
        foreach (var probe in new[]
                 {
                     Path.Combine(pkg, "node_modules", "@openai"),
                     Path.Combine(npmRoot, "node_modules", "@openai"),
                 })
        {
            if (!Directory.Exists(probe)) continue;
            foreach (var platformPkg in Directory.EnumerateDirectories(probe, "codex-win32-*"))
            {
                var vendor = Path.Combine(platformPkg, "vendor");
                if (!Directory.Exists(vendor)) continue;
                foreach (var triple in Directory.EnumerateDirectories(vendor))
                {
                    var exe = Path.Combine(triple, "bin", "codex.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        return null;
    }

    public static (string App, string[] Args) BuildLaunch(string codex, params string[] extraArgs)
    {
        if (codex.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", codex };
            args.AddRange(extraArgs);
            return ("pwsh.exe", args.ToArray());
        }

        if (codex.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (codex, extraArgs);

        var cmdArgs = new List<string> { "/d", "/c", codex };
        cmdArgs.AddRange(extraArgs);
        return (Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmdArgs.ToArray());
    }
}

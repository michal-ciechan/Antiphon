using System.Runtime.InteropServices;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Opt-in gate for CARD-0168 real-CLI × stub-proxy canaries. Dedicated flag
/// <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c> — do NOT reuse <c>ANTIPHON_HEADED_TESTS</c> /
/// <c>ANTIPHON_CODEX_HEADED_TESTS</c> (that would silently enroll headed runs into spawning
/// real paid CLIs).
/// </summary>
internal static class RealCliStubGate
{
    public const string EnvFlag = "ANTIPHON_REAL_CLI_STUB_TESTS";

    public static void SkipIfNotEligible(AgentKind kind)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Real-CLI stub-proxy canaries require Windows");

        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
        {
            throw new SkipTestException(
                $"Set {EnvFlag}=1 to opt in to real-CLI stub-proxy canaries " +
                $"(distinct from ANTIPHON_HEADED_TESTS / ANTIPHON_CODEX_HEADED_TESTS)");
        }

        switch (kind)
        {
            case AgentKind.ClaudeCode:
                if (ResolveClaude() is null)
                    throw new SkipTestException("claude not found on PATH; cannot run Claude stub-proxy canary");
                break;
            case AgentKind.Grok:
                if (ResolveGrok() is null)
                    throw new SkipTestException("grok not found on PATH; cannot run Grok stub-proxy canary");
                if (!GrokLocalLoginPresent())
                {
                    throw new SkipTestException(
                        "Grok local login (~/.grok/auth.json) not present. Log in with `grok` once; " +
                        "CARD-0168 A-tier Grok requires intact OAuth for a clean turn.");
                }
                break;
            case AgentKind.Codex:
                if (HeadedCodexGate.ResolveCodex() is null)
                    throw new SkipTestException("codex not found; cannot run Codex stub-proxy canary");
                break;
            default:
                throw new SkipTestException($"No real-CLI stub-proxy canary for kind {kind}");
        }
    }

    public static string ResolveClaudeOrThrow()
        => ResolveClaude() ?? throw new InvalidOperationException("claude not found on PATH");

    public static string ResolveGrokOrThrow()
        => ResolveGrok() ?? throw new InvalidOperationException("grok not found on PATH");

    public static string? ResolveClaude() => HeadedClaudeGate.ResolveClaude();

    public static string? ResolveGrok()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "grok.exe", "grok.cmd", "grok.bat", "grok.ps1" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var bundled = Path.Combine(home, ".grok", "bin", "grok.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    public static bool GrokLocalLoginPresent()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var auth = Path.Combine(home, ".grok", "auth.json");
        return File.Exists(auth);
    }

    /// <summary>
    /// CARD-0168 S5 composed gate: herdr pipe must answer. Skip, never fail, when herdr is
    /// absent — matching the design's "herdr absent ⇒ skip, not fail" rule.
    /// </summary>
    public static async Task SkipIfHerdrUnreachableAsync(CancellationToken cancellationToken)
    {
        var client = new Antiphon.SessionRunner.HerdrClient(
            Options.Create(new Antiphon.SessionRunner.HerdrSettings { Enabled = true }));
        try
        {
            await client.ConnectAndValidateAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not SkipTestException and not OperationCanceledException)
        {
            throw new SkipTestException(
                "Live herdr is not reachable (pipe did not answer ping). "
                + "CARD-0168 S5 B-herdr cell skips rather than fails. "
                + $"Start herdr, or set SessionRunner:Herdr:Enabled=true on a running instance. "
                + $"Detail: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

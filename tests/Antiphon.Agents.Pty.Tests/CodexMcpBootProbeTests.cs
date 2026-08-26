using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0195: measures how long the real Codex TUI spends in its own bundled MCP-server bootstrap
/// ("Booting MCP server: codex_apps" / "Starting MCP servers (0/2): codex_apps, node_repl") before
/// the composer will accept input, and whether launch-time config overrides can skip it.
///
/// <para>Why a probe and not an assertion-heavy canary: CARD-0194's delegate (session
/// <c>8be1afc5</c>, 2026-08-25 02:40Z) had its boot prompt typed into the composer while that
/// status line was on screen and nothing ever submitted — the task died on a 10-minute watchdog
/// with the text still sitting there, and the MCP bootstrap was the obvious suspect. Before
/// suppressing those servers on every delegate launch, "how long does this normally take" needed a
/// number rather than one incident.</para>
///
/// <para>Spends NO model turns: these tests only ever observe the boot and type a marker into the
/// composer; nothing is ever submitted. Headed and <c>[Explicit]</c> all the same, because they
/// launch the operator's real CLI.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0195")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexMcpBootProbeTests
{
    private const string McpBootMarkerA = "Starting MCP server";
    private const string McpBootMarkerB = "Booting MCP server";

    /// <summary>Baseline: the launch shape Antiphon uses today, in a scratch working directory.</summary>
    [Test]
    public async Task Baseline_scratch_cwd() => await ProbeAsync(nameof(Baseline_scratch_cwd), [], null, 3);

    /// <summary>
    /// The incident's shape: the repo working directory plus the two <c>-c</c> overrides
    /// <see cref="Antiphon.Agents.Pty.Tests"/>' production sibling sends (reasoning effort and a
    /// developer-instructions bundle). The repo cwd matters because Codex enumerates the project.
    /// </summary>
    [Test]
    public async Task Baseline_repo_cwd_incident_shape() => await ProbeAsync(
        nameof(Baseline_repo_cwd_incident_shape),
        ["-c", "model_reasoning_effort=high", "-c", "developer_instructions=You are a delegate."],
        RepoRoot(),
        3);

    /// <summary>
    /// The candidate suppression: <c>-c mcp_servers.node_repl.enabled=false</c> (the key
    /// <c>codex mcp get node_repl</c> prints as <c>enabled: true</c>) plus <c>--disable apps</c>
    /// (the <c>apps</c> feature flag <c>codex features list</c> reports as stable/true, which is
    /// what brings up <c>codex_apps</c>).
    /// </summary>
    [Test]
    public async Task Suppressed_repo_cwd() => await ProbeAsync(
        nameof(Suppressed_repo_cwd),
        ["-c", "mcp_servers.node_repl.enabled=false", "--disable", "apps",
         "-c", "model_reasoning_effort=high"],
        RepoRoot(),
        3);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static async Task ProbeAsync(string name, string[] extra, string? cwdOverride, int iterations)
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(name);

        for (var i = 1; i <= iterations; i++)
        {
            var cwd = cwdOverride ?? CxSession.TempCwd();
            var marker = "MCPPROBE-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                var (app, args) = CxSession.BuildLaunch(CxSession.ResolveCli()!, extra);
                log($"--- iteration {i} --- LAUNCH {app} {string.Join(' ', args)} (cwd {cwd})");
                await using var runner = new PtyAgentRunner("modern");
                var started = DateTime.UtcNow;
                await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 34, env: CxSession.HeadedEnv());
                runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty, "Reason: " + runner.Backend!.Reason);

                DateTime? mcpFirstSeen = null, mcpLastSeen = null, headerSeen = null;
                var deadline = started + TimeSpan.FromMinutes(6);
                var lastAnswer = DateTime.MinValue;

                while (DateTime.UtcNow < deadline)
                {
                    var screen = runner.SnapshotScreen();
                    var modal = screen.Contains("Press enter to continue", StringComparison.Ordinal);

                    if (screen.Contains(McpBootMarkerA, StringComparison.Ordinal)
                        || screen.Contains(McpBootMarkerB, StringComparison.Ordinal))
                    {
                        mcpFirstSeen ??= DateTime.UtcNow;
                        mcpLastSeen = DateTime.UtcNow;
                    }

                    if (headerSeen is null && screen.Contains("OpenAI Codex", StringComparison.Ordinal) && !modal)
                        headerSeen = DateTime.UtcNow;

                    if (modal && DateTime.UtcNow - lastAnswer > TimeSpan.FromSeconds(1))
                    {
                        lastAnswer = DateTime.UtcNow;
                        if (screen.Contains("Update available", StringComparison.OrdinalIgnoreCase))
                        {
                            // NOT Enter: option 1 upgrades the CLI out from under the session.
                            await runner.WriteAsync("2");
                            await Task.Delay(200);
                            await runner.WriteAsync("\r");
                        }
                        else if (CodexTrustPromptDetector.IsVisible(runner.SnapshotText(), screen))
                        {
                            await runner.WriteAsync("\r");
                        }
                    }

                    // Stop once the composer is up and the boot status has been gone for 4s, or
                    // after 20s of never having seen it at all.
                    if (headerSeen is not null)
                    {
                        if (mcpLastSeen is { } last && DateTime.UtcNow - last > TimeSpan.FromSeconds(4))
                            break;
                        if (mcpLastSeen is null && DateTime.UtcNow - started > TimeSpan.FromSeconds(20))
                            break;
                    }

                    await Task.Delay(60);
                }

                var toHeader = headerSeen is { } h ? (h - started).TotalSeconds : double.NaN;
                var mcpStart = mcpFirstSeen is { } f ? (f - started).TotalSeconds : double.NaN;
                var mcpEnd = mcpLastSeen is { } l ? (l - started).TotalSeconds : double.NaN;
                log($"RESULT[{i}] mcp_status_seen={mcpFirstSeen is not null} first_at={mcpStart:F2}s "
                    + $"last_at={mcpEnd:F2}s visible_for={(mcpFirstSeen is not null ? mcpEnd - mcpStart : 0):F2}s "
                    + $"header_at={toHeader:F2}s");

                // Prove the composer accepts and echoes typed text once the status has cleared.
                await runner.WriteAsync(marker);
                await Task.Delay(1500);
                var after = runner.SnapshotScreen();
                log($"COMPOSER ECHO[{i}]: " + (after.Contains(marker, StringComparison.Ordinal) ? "yes" : "NO"));
                if (i == 1)
                    log("SCREEN:\n" + CxSession.Tail(after, 1600));

                await runner.KillAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                if (cwdOverride is null)
                    CxSession.BestEffortDelete(cwd);
            }
        }
    }
}

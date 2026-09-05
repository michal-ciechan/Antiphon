using System.Diagnostics;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0133 S0.  Real interactive Codex, the production PtyHost lane and a local responses stub.
/// It deliberately records observations instead of asserting a wedge rate: the rate is the result.
/// </summary>
[Explicit]
[Category("Headed")]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public sealed class CodexBootWedgeProbeTests
{
    private const int DefaultIterations = 30;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SubmitObservation = TimeSpan.FromSeconds(30);

    [Test]
    [Timeout(3_600_000)]
    public async Task P1_plain_ptyhost_production_shape_measures_boot_wedge(CancellationToken ct)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);
        var iterations = ReadPositiveInt("ANTIPHON_CARD0133_ITERATIONS", DefaultIterations);
        var executable = Environment.GetEnvironmentVariable("ANTIPHON_CARD0133_CODEX_EXE");
        var outcomes = new List<ProbeOutcome>();

        for (var i = 1; i <= iterations; i++)
        {
            var outcome = await RunSubmitProbeAsync(executable, i, ct);
            outcomes.Add(outcome);
            Console.WriteLine(outcome.Render());
        }

        var wedges = outcomes.Count(o => o.Wedged);
        var submitted = outcomes.Count(o => o.Submitted);
        Console.WriteLine(
            $"CARD-0133 P1 RESULT version={VersionLabel(executable)} launches={outcomes.Count} "
            + $"wedges={wedges} ({Percent(wedges, outcomes.Count):F1}%) submitted={submitted} "
            + $"other={outcomes.Count - wedges - submitted}");
    }

    /// <summary>
    /// P3 uses the same production-shaped probe.  Set ANTIPHON_CARD0133_CODEX_EXE to the native
    /// executable beneath a scratch <c>npm install --prefix &lt;scratch&gt; @openai/codex@0.149.1</c>;
    /// this never changes the global Codex shim.  The normal P1 command is then re-run unchanged.
    /// </summary>
    [Test]
    [Timeout(3_600_000)]
    public async Task P3_scratch_01491_path_measures_boot_wedge(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_CARD0133_RUN_P3") != "1")
            return;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTIPHON_CARD0133_CODEX_EXE")))
            throw new InvalidOperationException(
                "P3 requires ANTIPHON_CARD0133_CODEX_EXE resolved from the scratch 0.149.1 npm prefix; "
                + "refusing to measure the global Codex shim.");
        await P1_plain_ptyhost_production_shape_measures_boot_wedge(ct);
    }

    [Test]
    [Timeout(600_000)]
    public async Task P4_measures_clear_keys_and_boot_paste_interrupt(CancellationToken ct)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);
        var clears = new (string Name, Func<int, string> Input)[]
        {
            ("Backspace", count => new string('\b', count)),
            ("Ctrl+U", _ => "\x15"),
            ("Ctrl+A+Ctrl+K", _ => "\x01\x0b"),
            ("Esc", _ => "\x1b"),
        };

        foreach (var clear in clears)
        {
            var result = await RunClearProbeAsync(clear.Name, clear.Input, ct);
            Console.WriteLine(result);
        }

        Console.WriteLine(await RunMcpInterruptProbeAsync(ct));
    }

    private static async Task<ProbeOutcome> RunSubmitProbeAsync(string? executable, int iteration, CancellationToken ct)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"card0133-p1-{Guid.NewGuid():N}");
        var cwd = Path.Combine(tempRoot, "cwd");
        var logs = Path.Combine(tempRoot, "logs");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(codexHome);
        var sessionId = Guid.NewGuid();
        var marker = $"CARD0133-{Guid.NewGuid():N}";
        var body = BuildPointerBody(marker);
        var diagnostics = new List<string>();

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn("stub reply"));
        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, $"stub-codex-{Guid.NewGuid():N}", codexHome);
        var env = new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase)
        {
            ["RUST_LOG"] = "codex_tui=debug,codex_core=debug",
            // Windows hosts often leave TERM unset; this Grok/agent shell sets TERM=dumb, and
            // Codex 0.151.0 then prompts "Continue anyway?" and refuses the TUI. Production
            // PtyHost launches don't inherit that; pin a real type so P1 measures PasteBurst.
            ["TERM"] = "xterm-256color",
        };
        var (app, launchArgs) = BuildInteractiveLaunch(executable);
        var args = new List<string>(launchArgs)
        {
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox",
        };
        args.AddRange(overlay.Args);
        // CARD-0133: the production launch path now carries this on every Codex process. P1 is
        // measuring that path, not the pre-fix 20 ms gap against a PasteBurst-armed TUI.
        args.AddRange([CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DisablePasteBurst]);

        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = logs,
                PtyHostLingerHours = 0.02,
                PtyBackend = "modern",
            }),
            NullLogger<SessionRunnerRuntime>.Instance);

        try
        {
            var started = await runtime.StartAsync(new RunnerLaunchRequest(
                sessionId, app, args, env, cwd, 120, 30,
                TranscriptEnabled: true, TranscriptFormat: TranscriptFormats.Codex,
                Backend: SessionBackends.PtyHost), ct);
            if (started.Status != "Running")
                return Persist(new ProbeOutcome(iteration, false, false, $"start status={started.Status}"));

            await WaitForProductionReadyAsync(runtime, sessionId, ct);
            await runtime.SendInputAsync(sessionId, PtyInputEncoding.WrapIfMultiline(PtyInputEncoding.NormalizeBody(body)), ct);
            if (!await WaitForComposerEvidenceAsync(runtime, sessionId, marker, ct))
                return Persist(new ProbeOutcome(iteration, false, false, "no-composer-evidence: marker never rendered; Enter not sent"));
            var beforeEnter = runtime.GetSnapshot(sessionId).LastSequence;
            await Task.Delay(20, ct);
            await runtime.SendInputAsync(sessionId, "\r", ct);
            var submit = await ObserveSubmitAsync(runtime, sessionId, marker, beforeEnter, ct);
            if (submit.Submitted)
                return Persist(new ProbeOutcome(iteration, true, false, submit.Detail));

            diagnostics.AddRange(await DiagnoseWedgeAsync(runtime, sessionId, codexHome, logs, ct));
            return Persist(new ProbeOutcome(iteration, false, true, string.Join(" | ", diagnostics)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add($"exception={ex.GetType().Name}: {ex.Message}");
            diagnostics.AddRange(ReadDiagnostics(codexHome, logs));
            return Persist(new ProbeOutcome(iteration, false, false, string.Join(" | ", diagnostics)));
        }
        finally
        {
            try { await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None); } catch { }
            if (Environment.GetEnvironmentVariable("ANTIPHON_CARD0133_KEEP_ARTIFACTS") != "1")
                RealCliStubBServerHarness.TryDelete(tempRoot);
            else
                Console.WriteLine($"CARD-0133 artifacts={tempRoot}");
        }

        ProbeOutcome Persist(ProbeOutcome outcome)
        {
            try { File.WriteAllText(Path.Combine(tempRoot, "probe-result.txt"), outcome.Render()); } catch { }
            return outcome;
        }
    }

    private static async Task WaitForProductionReadyAsync(SessionRunnerRuntime runtime, Guid sessionId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        long? lastSequence = null;
        DateTime? quietSince = null;
        var trustAnswered = false;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = runtime.GetSnapshot(sessionId);
            // Answer the trust prompt ONCE, as RunnerCodexAdapter does. RawOutput is cumulative, so
            // the detector stays true after the dialog is gone; re-sending Enter on every poll walked
            // the Windows sandbox NUX prompt onto its elevated-setup default (CARD-0133 S0).
            if (!trustAnswered && CodexTrustPromptDetector.IsVisible(snapshot.RawOutput, snapshot.RenderedScreen))
            {
                trustAnswered = true;
                await runtime.SendInputAsync(sessionId, "\r", ct);
            }
            if (snapshot.LastSequence != lastSequence)
            {
                lastSequence = snapshot.LastSequence;
                quietSince = DateTime.UtcNow;
            }
            else if (quietSince is not null && DateTime.UtcNow - quietSince >= TimeSpan.FromSeconds(1)
                     && !string.IsNullOrWhiteSpace(snapshot.RenderedScreen))
            {
                return;
            }
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("production Codex ready semantics did not become quiet after visible output");
    }

    private static async Task<(bool Submitted, string Detail)> ObserveSubmitAsync(
        SessionRunnerRuntime runtime, Guid sessionId, string marker, long beforeEnter, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + SubmitObservation;
        await Task.Delay(500, ct); // body's own trailing frames must not count as the submit.
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = runtime.GetSnapshot(sessionId);
            var transcript = runtime.GetTranscript(sessionId);
            var userRow = transcript.Entries.Any(e => e.Kind == TranscriptKinds.UserPrompt
                && e.Text?.Contains(marker, StringComparison.Ordinal) == true);
            var markerVisible = snapshot.RenderedScreen.Contains(marker, StringComparison.Ordinal);
            var working = CodexWorkingIndicator.IsVisible(snapshot.RenderedScreen);
            if (userRow || working || (!markerVisible && snapshot.LastSequence > beforeEnter))
                return (true, $"submitted userRow={userRow} working={working} markerVisible={markerVisible} seq={snapshot.LastSequence}");
            await Task.Delay(250, ct);
        }
        return (false, $"no-positive-submit marker-still-visible={runtime.GetSnapshot(sessionId).RenderedScreen.Contains(marker, StringComparison.Ordinal)}");
    }

    private static async Task<bool> WaitForComposerEvidenceAsync(
        SessionRunnerRuntime runtime, Guid sessionId, string marker, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (runtime.GetSnapshot(sessionId).RenderedScreen.Contains(marker, StringComparison.Ordinal))
                return true;
            await Task.Delay(250, ct);
        }
        return false;
    }

    private static async Task<IReadOnlyList<string>> DiagnoseWedgeAsync(
        SessionRunnerRuntime runtime, Guid sessionId, string codexHome, string logs, CancellationToken ct)
    {
        var lines = new List<string>();
        var before = runtime.GetSnapshot(sessionId).LastSequence;
        await runtime.SendInputAsync(sessionId, "x", ct);
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        var afterChar = runtime.GetSnapshot(sessionId);
        lines.Add($"P2 char-renders={afterChar.RenderedScreen.Contains("x", StringComparison.Ordinal)} seq={before}->{afterChar.LastSequence}");
        await runtime.ResizeAsync(sessionId, 121, 30, ct);
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        var afterResize = runtime.GetSnapshot(sessionId);
        lines.Add($"P2 resize-sequence={afterChar.LastSequence}->{afterResize.LastSequence}");
        if (runtime.Get(sessionId).Pid is { } pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                var cpu = process.TotalProcessorTime;
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                process.Refresh();
                lines.Add($"P2 cpu-delta-ms={(process.TotalProcessorTime - cpu).TotalMilliseconds:F0}");
            }
            catch (Exception ex) { lines.Add($"P2 cpu={ex.GetType().Name}"); }
        }
        await runtime.SendInputAsync(sessionId, "\x1b", ct);
        await Task.Delay(500, ct);
        await runtime.SendInputAsync(sessionId, "\x03", ct);
        lines.AddRange(ReadDiagnostics(codexHome, logs));
        return lines;
    }

    private static async Task<string> RunClearProbeAsync(string name, Func<int, string> clear, CancellationToken ct)
    {
        var result = await WithFreshRuntimeAsync(async (runtime, sessionId) =>
        {
            var token = "zz" + Guid.NewGuid().ToString("N")[..8];
            await runtime.SendInputAsync(sessionId, token, ct);
            await Task.Delay(750, ct);
            var typed = runtime.GetSnapshot(sessionId).RenderedScreen.Contains(token, StringComparison.Ordinal);
            await runtime.SendInputAsync(sessionId, clear(token.Length), ct);
            await Task.Delay(750, ct);
            var cleared = !runtime.GetSnapshot(sessionId).RenderedScreen.Contains(token, StringComparison.Ordinal);
            var follow = "zz" + Guid.NewGuid().ToString("N")[..8];
            await runtime.SendInputAsync(sessionId, follow, ct);
            await Task.Delay(750, ct);
            var followUp = runtime.GetSnapshot(sessionId).RenderedScreen.Contains(follow, StringComparison.Ordinal);
            return $"CARD-0133 P4 clear={name} typed={typed} cleared={cleared} followUp={followUp}";
        }, ct);
        return result;
    }

    private static async Task<string> RunMcpInterruptProbeAsync(CancellationToken ct) => await WithFreshRuntimeAsync(
        async (runtime, sessionId) =>
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            var bootSeen = false;
            while (DateTime.UtcNow < deadline)
            {
                var screen = runtime.GetSnapshot(sessionId).RenderedScreen;
                if (screen.Contains("Booting MCP server", StringComparison.Ordinal)
                    || screen.Contains("Starting MCP servers", StringComparison.Ordinal))
                {
                    bootSeen = true;
                    var body = "MCPPASTE-" + Guid.NewGuid().ToString("N");
                    await runtime.SendInputAsync(sessionId, PtyInputEncoding.WrapIfMultiline(body + "\nline"), ct);
                    await Task.Delay(1500, ct);
                    var interrupted = runtime.GetSnapshot(sessionId).RenderedScreen.Contains("MCP startup interrupted", StringComparison.OrdinalIgnoreCase);
                    return $"CARD-0133 P4 mcp-boot-seen=true interrupted={interrupted}";
                }
                await Task.Delay(100, ct);
            }
            return $"CARD-0133 P4 mcp-boot-seen={bootSeen} interrupted=unmeasured";
        }, ct);

    private static async Task<T> WithFreshRuntimeAsync<T>(Func<SessionRunnerRuntime, Guid, Task<T>> action, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), $"card0133-p4-{Guid.NewGuid():N}");
        var cwd = Path.Combine(root, "cwd");
        var home = Path.Combine(root, "codex-home");
        Directory.CreateDirectory(cwd);
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, $"stub-codex-{Guid.NewGuid():N}", home);
        var env = new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase) { ["RUST_LOG"] = "codex_tui=debug,codex_core=debug" };
        var (app, launchArgs) = BuildInteractiveLaunch(null);
        var args = new List<string>(launchArgs) { "--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox" };
        args.AddRange(overlay.Args);
        await using var runtime = new SessionRunnerRuntime(Options.Create(new SessionRunnerSettings { SessionLogPath = Path.Combine(root, "logs"), PtyHostLingerHours = 0.02, PtyBackend = "modern" }), NullLogger<SessionRunnerRuntime>.Instance);
        var id = Guid.NewGuid();
        try
        {
            await runtime.StartAsync(new RunnerLaunchRequest(id, app, args, env, cwd, 120, 30, TranscriptEnabled: true, TranscriptFormat: TranscriptFormats.Codex, Backend: SessionBackends.PtyHost), ct);
            await WaitForProductionReadyAsync(runtime, id, ct);
            return await action(runtime, id);
        }
        finally
        {
            try { await runtime.KillAsync(id, TimeSpan.FromSeconds(5), CancellationToken.None); } catch { }
            RealCliStubBServerHarness.TryDelete(root);
        }
    }

    private static IReadOnlyList<string> ReadDiagnostics(string codexHome, string logs)
    {
        var lines = new List<string>();
        var tuiLog = Path.Combine(codexHome, "log", "codex-tui.log");
        if (File.Exists(tuiLog))
            lines.Add("codex-tui-tail=" + Tail(File.ReadAllLines(tuiLog), 40));
        var ptyLogs = Directory.Exists(logs) ? Directory.EnumerateFiles(logs, "*.log", SearchOption.AllDirectories).ToList() : [];
        if (ptyLogs.Count > 0)
            lines.Add("pty-log-tail=" + Tail(File.ReadAllLines(ptyLogs[0]), 20));
        return lines;
    }

    private static string Tail(IEnumerable<string> lines, int count) => string.Join(" / ", lines.TakeLast(count)).Replace("\r", " ").Replace("\n", " ");
    private static string BuildPointerBody(string marker) => ("Read .antiphon/task-" + marker + "-brief.md and follow it. " + new string('p', 700))[..620];
    private static (string App, string[] Args) BuildInteractiveLaunch(string? executable) => string.IsNullOrWhiteSpace(executable) ? HeadedCodexGate.BuildLaunch(HeadedCodexGate.ResolveOrThrow()) : HeadedCodexGate.BuildLaunch(executable);
    private static int ReadPositiveInt(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
    private static double Percent(int numerator, int denominator) => denominator == 0 ? 0 : numerator * 100d / denominator;
    private static string VersionLabel(string? executable) => string.IsNullOrWhiteSpace(executable) ? "global" : executable;
    private sealed record ProbeOutcome(int Iteration, bool Submitted, bool Wedged, string Detail)
    {
        public string Render() => $"CARD-0133 P1 iteration={Iteration} submitted={Submitted} wedged={Wedged} {Detail}";
    }
}

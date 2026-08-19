using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Exercises CodexAdapter against a real local cmd.exe. This keeps the test
/// offline while proving the adapter can start, send input, detect a quiet
/// completed turn, detect questions, and stop the underlying process.
/// </summary>
[NotInParallel("Pty")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexAdapterLocalShellTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static AgentLaunchSpec InteractiveCmdSpec() => new(
        DefinitionName: "codex-fake",
        Kind: AgentKind.Codex,
        Exe: Cmd,
        Args: new[] { "/d", "/q", "/k", "@echo off & prompt $G" },
        Env: new Dictionary<string, string>(),
        Cwd: Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    private static AgentLaunchSpec TrustPromptCmdSpec(string batchPath) => new(
        DefinitionName: "codex-fake",
        Kind: AgentKind.Codex,
        Exe: Cmd,
        Args: new[] { "/d", "/q", "/k", batchPath },
        Env: new Dictionary<string, string>(),
        Cwd: Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    // CARD-0050 S2 wait-window inventory. Quiet periods are the already-measured ConPTY-echo
    // floor (250ms declared ready/done in the latency gap before cmd's output arrived, ~2/3 of
    // loaded full-suite runs). MaxWaits are runaway bounds — success returns on quiet, so a
    // generous ceiling costs nothing. They are NOT the lever for a late-starting child:
    // CodexReadyDetector / WaitForQuietAsync treat empty+quiet as ready, so they return true
    // after QuietPeriod of ZERO output (or of title-only OSC) and MaxWait never runs.
    //
    // Measured under the concurrent double-suite load (2026-08-19, this slice):
    //   - before: 3 tests in this file finished in 1.74–3.06s with snapshot "" — first
    //     body output was strictly later than QuietPeriod, so ready/done fired empty.
    //   - after a any-byte gate: first ConPTY write at 2321ms was the cmd TITLE
    //     (ESC]0;cmd.exe - <bat>…), and the batch body was STILL absent at 6549ms
    //     (title→body gap >4.2s — the CARD-0015 shape). Gating on any byte just
    //     moved the false ready from empty to title-only.
    // Stretching QuietPeriod would only delay that same false ready. Expected-text
    // gates (WaitUntilSnapshotContainsAsync) wait for the body the assertion needs.
    private static IOptions<AgentRegistrySettings> FastOptions() => Options.Create(new AgentRegistrySettings
    {
        DefaultDefinition = "codex-fake",
        Definitions = { ["codex-fake"] = new AgentDefinition { Kind = "Codex", Exe = Cmd } },
        CodexReadyQuietPeriodMs = 750,   // scenario-gated: must outlast ConPTY echo (250ms flaked)
        CodexReadyMaxWaitMs = 60_000,    // runaway bound (was 15s); success returns on quiet
        CodexDoneQuietPeriodMs = 750,    // scenario-gated: same echo floor as ready
        CodexDoneMaxWaitMs = 60_000,     // runaway bound (was 15s); success returns on quiet
    });

    /// <summary>
    /// CARD-0050 S2: poll until <paramref name="needle"/> is in the raw snapshot.
    /// Runaway bound — success returns on the match. Used so WaitForReady/WaitForTurnComplete
    /// cannot fire on empty or title-only output (cmd writes the OSC title before the body).
    /// </summary>
    private static async Task WaitUntilSnapshotContainsAsync(
        CodexAdapter adapter, string needle, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var snap = adapter.SnapshotRawOutput();
            if (snap.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"[CARD-0050 S2] saw '{needle}' after {sw.ElapsedMilliseconds}ms (len {snap.Length})");
                return;
            }
            await Task.Delay(50);
        }

        throw new System.TimeoutException(
            $"cmd snapshot did not contain '{needle}' in {timeout.TotalSeconds:0}s " +
            $"(length {adapter.SnapshotRawOutput().Length}).");
    }

    [Test]
    public async Task Wait_for_turn_complete_returns_question_state_after_quiet_output()
    {
        SkipIfNotWindows();
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);
        await WaitUntilSnapshotContainsAsync(adapter, ">", TimeSpan.FromSeconds(60));
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        await adapter.SendPromptAsync("echo Should we continue?", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.RawSnapshot.ShouldContain("Should we continue?");
        result.ResponseText.ShouldNotBeNull();
        result.ResponseText.ShouldContain("Should we continue?");
        result.IsAskingQuestion.ShouldBeTrue();
    }

    [Test]
    public async Task Wait_for_ready_accepts_codex_directory_trust_prompt()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch("""
            @echo off
            echo Do you trust the contents of this directory?
            echo 1. Yes, continue
            set /p CHOICE=
            echo READY_AFTER_TRUST
            prompt $G
            """);
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(TrustPromptCmdSpec(bat.Path), CancellationToken.None);
        // Title-only is not enough: cmd writes ESC]0;… before the batch body
        // (measured 2321ms to title, body still absent at 6549ms under load).
        await WaitUntilSnapshotContainsAsync(adapter, "1. Yes, continue", TimeSpan.FromSeconds(60));

        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);

        ready.ShouldBeTrue();
        // WaitForReady sends Enter on the trust prompt then waits QuietPeriod;
        // the accept echo can land after that quiet, so gate on it separately.
        await WaitUntilSnapshotContainsAsync(adapter, "READY_AFTER_TRUST", TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task Question_detection_ignores_question_mark_in_prompt_echo()
    {
        SkipIfNotWindows();
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);
        await WaitUntilSnapshotContainsAsync(adapter, ">", TimeSpan.FromSeconds(60));
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        await adapter.SendPromptAsync("echo answer has no question & rem prompt has a question?", CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue();
        result.ResponseText.ShouldNotBeNull();
        result.ResponseText.ShouldContain("answer has no question");
        result.IsAskingQuestion.ShouldBeFalse();
    }

    [Test]
    public async Task Kill_terminates_codex_process_with_stopped_exit_reason()
    {
        SkipIfNotWindows();
        await using var adapter = new CodexAdapter(FastOptions());
        await adapter.StartAsync(InteractiveCmdSpec(), CancellationToken.None);
        await WaitUntilSnapshotContainsAsync(adapter, ">", TimeSpan.FromSeconds(60));
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();

        await Task.Delay(300); // settle; not a deadline
        var sw = Stopwatch.StartNew();
        // KillAsync(2s) is a runaway bound (returns when the process dies). The 2.5s
        // assertion is scenario-gated — it pins kill being prompt. Do not widen it
        // without a measured slow kill under load.
        var killed = await adapter.KillAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        sw.Stop();

        killed.ShouldBeTrue();
        adapter.ExitReason.ShouldBe(AgentExitReason.KilledByRequest);
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
    }
}

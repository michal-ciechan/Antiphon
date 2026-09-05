using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
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
/// offline while proving the adapter can start, send input, detect a
/// completed turn, detect questions, and stop the underlying process.
///
/// <para><b>CARD-0108 S2 changed what "a completed turn" means here.</b> It used to be terminal
/// quiet alone, and against the real CLI that certified a prompt stranded in a silent composer as a
/// finished turn, handing the status bar back as the model's answer. A Codex turn is now recognised
/// by its measured lifecycle — the <c>Working ( … esc to interrupt)</c> indicator appearing and then
/// leaving the screen — so the turn tests below wrap their commands in
/// <see cref="AsCodexTurn"/> to render that lifecycle. cmd.exe produces no such indicator on its
/// own, and a fake that does not render one is now (correctly) never read as having taken a
/// turn.</para>
/// </summary>
[NotInParallel("Pty")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class CodexAdapterLocalShellTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    /// <summary>
    /// <c>cmd /d /k "prompt $G"</c>. It used to be <c>/d /q /k "@echo off &amp; prompt $G"</c>, and
    /// that fixture prints NO PROMPT AT ALL: measured 2026-08-20 through this same PTY, cmd runs the
    /// command (the OSC title shows <c>cmd.exe - echo  off</c>) and then emits nothing — with ECHO
    /// OFF an interactive cmd does not display its prompt. Every test gated on
    /// <see cref="WaitUntilSnapshotContainsAsync"/> seeing <c>"&gt;"</c> therefore timed out after
    /// 60 s on a 93-character snapshot that was just the title, which is how three of this class's
    /// tests were failing at the base commit before CARD-0108 touched anything. Echo stays ON now;
    /// an interactive cmd does not re-echo the commands you type, so the prompt still appears
    /// exactly once in the snapshot and <c>ExtractResponse</c> still strips it.
    /// </summary>
    private static AgentLaunchSpec InteractiveCmdSpec() => new(
        DefinitionName: "codex-fake",
        Kind: AgentKind.Codex,
        Exe: Cmd,
        Args: new[] { "/d", "/k", "prompt $G" },
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
    // generous ceiling costs nothing. CARD-0052 closed the empty/title-only hole: quiet
    // cannot count until HasVisibleOutput, so these existing tests still gate on the body
    // via WaitUntilSnapshotContainsAsync (cmd writes the OSC title before the batch body)
    // and the new slow-start tests pin ready/done themselves.
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
    /// Wraps a cmd command so the fake session models a real Codex TURN rather than merely some
    /// output: the <c>Working ( … esc to interrupt)</c> indicator is echoed, the work runs while it
    /// stands, and it is then SCROLLED off the rendered screen the way the real TUI takes it down
    /// when the turn completes. The middle <c>ping</c> keeps the indicator up for ~2 s — several
    /// 250 ms detector polls — so the lifecycle cannot be missed between two samples.
    ///
    /// <para>Scrolled rather than cleared on purpose: <c>cls</c> does NOT clear this screen. Under
    /// ConPTY cmd renders it as per-line <c>ESC[nX</c> erase-character runs, measured, which leave
    /// every earlier row (indicator included) exactly where it was. Forty echoed lines past a
    /// 30-row window is unambiguous. The bullet the real TUI draws is deliberately not reproduced —
    /// ConPTY narrows non-ASCII input and the matcher does not look for it — and the parens are
    /// caret-escaped for cmd.</para>
    /// </summary>
    private static string AsCodexTurn(string command) =>
        $"echo Working ^(1s - esc to interrupt^) & {command} & ping -n 3 127.0.0.1 > nul "
        + "& for /l %i in (1,1,40) do @echo .";

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

        await adapter.SendPromptAsync(
            AsCodexTurn("echo Should we continue?"), CancellationToken.None);
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

        await adapter.SendPromptAsync(
            AsCodexTurn("echo answer has no question & echo prompt has a question? > nul"),
            CancellationToken.None);
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

    private static IOptions<AgentRegistrySettings> SlowStartOptions() => Options.Create(new AgentRegistrySettings
    {
        DefaultDefinition = "codex-fake",
        Definitions = { ["codex-fake"] = new AgentDefinition { Kind = "Codex", Exe = Cmd } },
        CodexReadyQuietPeriodMs = 600,
        CodexReadyMaxWaitMs = 15_000,
        CodexDoneQuietPeriodMs = 600,
        CodexDoneMaxWaitMs = 15_000,
    });

    private static AgentLaunchSpec SlowStartCmdSpec(string batchPath) => new(
        DefinitionName: "codex-fake",
        Kind: AgentKind.Codex,
        Exe: Cmd,
        Args: new[] { "/d", "/q", "/k", batchPath },
        Env: new Dictionary<string, string>(),
        Cwd: Environment.CurrentDirectory,
        Cols: 120,
        Rows: 30);

    [Test]
    public async Task Wait_for_ready_does_not_fire_during_slow_start_silence()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch(
            "@echo off\r\nping -n 5 127.0.0.1 > nul\r\necho SLOW_START_BODY\r\nprompt $G\r\n");
        await using var adapter = new CodexAdapter(SlowStartOptions());
        await adapter.StartAsync(SlowStartCmdSpec(bat.Path), CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        sw.Stop();

        ready.ShouldBeTrue();
        adapter.SnapshotRawOutput().ShouldContain("SLOW_START_BODY");
        sw.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(2),
            "ready must not fire in the silent ping window");
    }

    [Test]
    public async Task Wait_for_turn_complete_does_not_succeed_on_a_stripped_empty_slow_start()
    {
        SkipIfNotWindows();
        using var bat = new PtyTempBatch(
            "@echo off\r\nping -n 5 127.0.0.1 > nul\r\necho SLOW_START_BODY\r\nprompt $G\r\n");
        await using var adapter = new CodexAdapter(SlowStartOptions());
        await adapter.StartAsync(SlowStartCmdSpec(bat.Path), CancellationToken.None);

        await adapter.SendPromptAsync(AsCodexTurn("cd ."), CancellationToken.None);
        var result = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        result.TurnCompleted.ShouldBeTrue(
            "the fake turn's Working indicator must have been seen and then scrolled away. Screen:\n"
            + adapter.SnapshotRenderedScreen());
        VisiblePtyOutput.HasVisibleOutput(result.RawSnapshot).ShouldBeTrue(
            "a completed empty turn is the card title — the snapshot must have visible text");
        result.RawSnapshot.ShouldContain("SLOW_START_BODY");
    }
}

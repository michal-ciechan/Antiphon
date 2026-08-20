using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0099 S1: headed canaries pinning the REAL Codex CLI's rollout shape, so a Codex update that
/// changes it goes red here instead of silently killing every Codex delegate's settlement.
///
/// <para>Ground truth is the rollout JSONL Codex writes to
/// <c>CODEX_HOME/sessions/YYYY/MM/DD/rollout-&lt;ts&gt;-&lt;uuid&gt;.jsonl</c>: the
/// <c>item_completed{UserMessage}</c> row is what the model actually received, and
/// <c>task_complete</c> is the explicit turn end <c>CodexTranscriptNormalizer</c> maps to
/// <c>TurnEnd</c>. The screen is only ever the thing being measured against it.</para>
///
/// <para><b>Every assertion here was measured before it was written</b> (2026-08-20, codex-cli
/// 0.147.0, real sessions through a modern ConPTY). They are pins on facts, not hopes:</para>
/// <list type="bullet">
/// <item>The interactive TUI writes <c>event_msg/item_completed</c> thread items, NOT the
/// <c>user_message</c>/<c>agent_message</c> rows <c>codex exec</c> and the Desktop app write. The
/// card's plan assumed one surface for both; if a future CLI unifies them the normalizer already
/// handles both, but this canary is what would report the change.</item>
/// <item>The rollout is created LAZILY at the first submit — 30 s of an idle rendered composer with
/// zero bytes written produced no file at all.</item>
/// <item>Enter on an empty composer submits nothing, which is the assumption CARD-0055's Enter-only
/// retry rests on before it may be turned on for a kind.</item>
/// <item>A typed <c>\n</c> is a literal newline in the composer and does not submit.</item>
/// <item>The TUI never prints its session id, so the tailer cannot take an exact bind off the
/// screen and must run the CARD-0006 discovery rules. (<c>codex exec</c> DOES print
/// <c>session id: &lt;uuid&gt;</c> — that is what made this worth checking rather than assuming.)</item>
/// </list>
///
/// <para>Headed, opt-in (<c>ANTIPHON_CODEX_HEADED_TESTS=1</c>), <c>[Explicit]</c>: the turn test
/// spends a real Codex turn against the operator's ChatGPT quota. The two structural tests spend
/// none — they only ever type into a composer and never submit — but stay in the same opt-in group
/// because they still launch the real CLI.</para>
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("HeadedCanary")]
[Category("Card0099")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexCanaryTests
{
    /// <summary>
    /// The record surface a real Antiphon-shaped Codex launch produces for one turn, and the
    /// mapping S1 built on it. If <c>task_complete</c> ever stops arriving, a Codex delegate can
    /// never settle — no TurnEnd row, no queue flush, no report extraction — which is the single
    /// failure this canary exists to catch early.
    /// </summary>
    [Test]
    public async Task One_real_turn_writes_UserMessage_AgentMessage_token_count_and_task_complete()
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(nameof(One_real_turn_writes_UserMessage_AgentMessage_token_count_and_task_complete));
        var cwd = CxSession.TempCwd();
        var before = new HashSet<string>(CxSession.Rollouts(), StringComparer.OrdinalIgnoreCase);
        var marker = "CX-" + Guid.NewGuid().ToString("N")[..12];

        try
        {
            var (app, args) = CxSession.BuildLaunch(CxSession.ResolveCli()!, "-m", "gpt-5.6-luna");
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 34);
            runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty,
                "the deployment runs modern; measuring the inbox conhost would measure the wrong pty. "
                + "Reason: " + runner.Backend!.Reason);

            (await CxSession.WaitForComposerAsync(runner, TimeSpan.FromSeconds(60)))
                .ShouldBeTrue("the composer must render. Screen:\n" + runner.SnapshotScreen());
            log("READY SCREEN:\n" + CxSession.Tail(runner.SnapshotScreen(), 1200));

            var body = $"Reply with exactly the token {marker} and nothing else.";
            await runner.WriteAsync(body);
            await Task.Delay(600);

            // The composer echo CARD-0055's evidence gate depends on. Measured: the typed body
            // renders in the composer row.
            runner.SnapshotScreen().ShouldContain(marker,
                customMessage: "the composer must echo the typed body — the CARD-0055 evidence gate "
                    + "withholds Enter without it. Screen:\n" + runner.SnapshotScreen());

            await runner.WriteAsync("\r");

            var rollout = await CxSession.WaitForNewRolloutAsync(before, TimeSpan.FromMinutes(2));
            rollout.ShouldNotBeNull("submitting must create a rollout. Screen:\n" + runner.SnapshotScreen());
            log($"ROLLOUT: {rollout}");

            var end = await CxSession.WaitForRowAsync(
                rollout!, r => r.EventType == "task_complete", TimeSpan.FromMinutes(3));
            end.ShouldNotBeNull(
                "task_complete is the ONLY structured turn end Codex writes; without it a Codex "
                + "delegate hangs at InProgress forever. Screen:\n" + runner.SnapshotScreen());

            var rows = CxSession.ReadRollout(rollout!);
            foreach (var r in rows)
                log($"  {r.Type}/{r.EventType}/{r.ItemType ?? "-"} {CxSession.Tail(r.Text ?? "", 60)}");

            // session_meta is line 0 and carries the cwd C2 matches on and the originator a refusal
            // reports — both are why C2/C3 are exact for Codex rather than heuristics.
            var meta = rows[0];
            meta.Type.ShouldBe("session_meta");
            meta.Cwd.ShouldNotBeNull();
            Path.GetFullPath(meta.Cwd!).ShouldBe(Path.GetFullPath(cwd));
            log($"originator={meta.Originator}");

            // The dialect. An Antiphon-shaped launch is the TUI, and the TUI writes thread items.
            rows.ShouldContain(r => r.EventType == "item_completed" && r.ItemType == "UserMessage"
                && r.Text!.Contains(marker),
                customMessage: "the TUI records the submitted prompt as item_completed{UserMessage}");
            rows.ShouldContain(r => r.EventType == "item_completed" && r.ItemType == "AgentMessage",
                customMessage: "the TUI records the answer as item_completed{AgentMessage}");
            rows.ShouldContain(r => r.EventType == "token_count",
                customMessage: "per-turn usage rides token_count; without it a delegate's cost is unstamped");

            rows.ShouldNotContain(r => r.EventType == "user_message",
                customMessage: "if the TUI starts writing the flat dialect too, the normalizer's "
                    + "dialect latch is the thing to re-check");

            // The prompt must land WHOLE: CARD-0055 confirmation compares the recorded text against
            // what was sent, and a composer that clipped it would certify a truncated delivery.
            var recorded = rows.First(r => r.ItemType == "UserMessage").Text!;
            await Assert.That(recorded).IsEqualTo(body);
        }
        finally
        {
            CxSession.BestEffortDelete(cwd);
        }
    }

    /// <summary>
    /// Two facts that cost no model turns, both load-bearing.
    ///
    /// <para><b>Lazy creation</b>: nothing is written until the first submit, so "no rollout yet" is
    /// the normal state of a healthy session and the tailer must not fault on it. If Codex ever
    /// starts creating the file eagerly, the discovery window changes and this goes red.</para>
    ///
    /// <para><b>Enter on an empty composer submits nothing</b>: CARD-0055's confirm loop re-presses
    /// Enter up to <c>SubmitAttempts</c> times, and it may only do that for a kind where a re-press
    /// on an already-empty composer is a no-op. Turning transcript-confirmed delivery on for Codex
    /// without this measured would be betting a double-send on an assumption.</para>
    /// </summary>
    [Test]
    public async Task The_rollout_is_lazy_and_Enter_on_an_empty_composer_submits_nothing()
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(nameof(The_rollout_is_lazy_and_Enter_on_an_empty_composer_submits_nothing));
        var cwd = CxSession.TempCwd();
        var before = new HashSet<string>(CxSession.Rollouts(), StringComparer.OrdinalIgnoreCase);

        try
        {
            var (app, args) = CxSession.BuildLaunch(CxSession.ResolveCli()!, "-m", "gpt-5.6-luna");
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 34);

            (await CxSession.WaitForComposerAsync(runner, TimeSpan.FromSeconds(60)))
                .ShouldBeTrue("the composer must render. Screen:\n" + runner.SnapshotScreen());

            // Phase A — nothing typed for 15s: still no rollout.
            await Task.Delay(TimeSpan.FromSeconds(15));
            var afterIdle = CxSession.Rollouts().Count(f => !before.Contains(f));
            log($"PHASE A rollouts after an idle rendered composer: {afterIdle}");
            afterIdle.ShouldBe(0,
                "the rollout is created lazily at the first submit; an eager file would change the "
                + "tailer's wait behaviour and the discovery window");

            // Phase B — five Enters on an empty composer must submit nothing.
            for (var i = 0; i < 5; i++)
            {
                await runner.WriteAsync("\r");
                await Task.Delay(400);
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
            var afterEnters = CxSession.Rollouts().Count(f => !before.Contains(f));
            log($"PHASE B rollouts after 5 empty Enters: {afterEnters}");
            await Assert.That(afterEnters)
                .IsEqualTo(0);
        }
        finally
        {
            CxSession.BestEffortDelete(cwd);
        }
    }

    /// <summary>
    /// Why the tailer has to run CARD-0006 discovery at all: the TUI never puts its session id on
    /// screen, so there is no positive id to read off and bind exactly with. <c>codex exec</c> DOES
    /// print <c>session id: &lt;uuid&gt;</c> at startup — which is exactly why this was measured
    /// rather than assumed. If a future TUI starts printing it, an exact bind becomes available and
    /// this canary is the signal to go and take it.
    ///
    /// <para>Also pins that a typed <c>\n</c> is a literal newline rather than a submit — the
    /// property the delivery queue's ReplaceLineEndings("\n") contract depends on. Costs no model
    /// turns: nothing is ever submitted.</para>
    /// </summary>
    [Test]
    public async Task The_TUI_does_not_print_its_session_id_and_a_typed_newline_does_not_submit()
    {
        CxSession.SkipIfNotEligible();
        var log = CxSession.MeasurementLog(nameof(The_TUI_does_not_print_its_session_id_and_a_typed_newline_does_not_submit));
        var cwd = CxSession.TempCwd();
        var before = new HashSet<string>(CxSession.Rollouts(), StringComparer.OrdinalIgnoreCase);

        try
        {
            var (app, args) = CxSession.BuildLaunch(CxSession.ResolveCli()!, "-m", "gpt-5.6-luna");
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(app, args, cwd: cwd, cols: 120, rows: 34);

            (await CxSession.WaitForComposerAsync(runner, TimeSpan.FromSeconds(60)))
                .ShouldBeTrue("the composer must render. Screen:\n" + runner.SnapshotScreen());

            // Everything the TUI has emitted, not just the visible 34 rows.
            var everything = runner.SnapshotText();
            log("FULL OUTPUT TAIL:\n" + CxSession.Tail(everything, 2000));
            everything.ShouldNotContain("session id",
                customMessage: "if the TUI starts printing its session id, CodexTranscriptTailer can "
                    + "take an EXACT bind off the screen and keep C1-C4 only as the guard");

            await runner.WriteAsync("CX-LINE-ONE alpha\nCX-LINE-TWO bravo\nCX-LINE-THREE charlie");
            await Task.Delay(1500);

            var screen = runner.SnapshotScreen();
            log("SCREEN AFTER MULTILINE LF (no Enter):\n" + CxSession.Tail(screen, 900));
            screen.ShouldContain("CX-LINE-ONE alpha");
            screen.ShouldContain("CX-LINE-THREE charlie",
                customMessage: "a typed \\n must be a literal newline in the composer, not a submit");

            await Assert.That(CxSession.Rollouts().Count(f => !before.Contains(f)))
                .IsEqualTo(0);
        }
        finally
        {
            CxSession.BestEffortDelete(cwd);
        }
    }
}

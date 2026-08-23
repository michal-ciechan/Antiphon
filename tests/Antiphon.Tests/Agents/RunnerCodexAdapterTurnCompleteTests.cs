using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0108 S2, mirroring <see cref="RunnerGrokAdapterTurnCompleteTests"/>:
/// <see cref="RunnerCodexAdapter.WaitForTurnCompleteAsync"/> takes its verdict from the tailed
/// rollout when rows exist — Codex writes an explicit <c>event_msg/task_complete</c> per turn — and
/// falls back to the screen only when it does not.
///
/// <para>The fallback is the part that carried the defect. It used to be bare
/// <c>WaitForQuietAfterVisible(3s)</c>, which over a prompt stranded in a silent composer returned
/// <c>TurnCompleted: true</c> in ~3.2 s and handed the status bar back as the answer. It is now
/// gated on the measured Working-indicator lifecycle, so the last test here — a session that never
/// visibly works — is red against the old code and honest against the new.</para>
/// </summary>
public class RunnerCodexAdapterTurnCompleteTests
{
    [Test]
    public async Task A_TurnEnd_row_past_the_prompt_baseline_completes_the_turn_with_transcript_reply_text()
    {
        var client = new ScriptedCodexRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing please", CancellationToken.None);
        client.Append(TranscriptKinds.AssistantText, "the reply from the rollout");
        client.Append(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var started = DateTime.UtcNow;
        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeTrue();
        turn.ResponseText.ShouldBe("the reply from the rollout");
        turn.IsAskingQuestion.ShouldBeFalse();
        // The quiet period is 60s and the screen never showed a Working indicator — only the
        // transcript could have produced this verdict.
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task The_reply_text_decides_IsAskingQuestion_not_the_screen()
    {
        var client = new ScriptedCodexRunnerClient
        {
            // The status bar and the composer's ghost hint text both live here, and the old
            // screen-scrape verdict read their punctuation as the agent asking a question.
            QuietScreen = "  > Summarize recent commits?\n  gpt-5.6-luna low · ~/tmp\n",
        };
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing please", CancellationToken.None);
        client.Append(TranscriptKinds.AssistantText, "Done. Nothing else to add.");
        client.Append(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.ResponseText.ShouldBe("Done. Nothing else to add.");
        turn.IsAskingQuestion.ShouldBeFalse(
            "a '?' on the screen is not the agent asking; the reply text is what decides");
    }

    [Test]
    public async Task A_TurnEnd_at_or_below_the_baseline_does_not_complete_the_new_turn()
    {
        var client = new ScriptedCodexRunnerClient();
        // The PREVIOUS turn's rows are already there when the new prompt is sent.
        client.Seed(
            Row(Guid.Empty, 1, TranscriptKinds.UserPrompt, text: "an older prompt of some length"),
            Row(Guid.Empty, 2, TranscriptKinds.TurnEnd, stopReason: "end_turn"));

        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 1_500);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("the brand new prompt", CancellationToken.None);

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        turn.TurnCompleted.ShouldBeFalse(
            "an old TurnEnd below the baseline is history, not this turn's completion");
    }

    [Test]
    public async Task A_first_turn_with_no_transcript_yet_still_uses_baseline_zero()
    {
        // Production first-ever turn: the rollout file does not exist, so the capture fetch
        // throws, and 0 is the correct floor. Once the file appears, this turn's rows (all of
        // them) must still complete the wait — CARD-0113 must not hold the first turn off
        // transcript verdicts just because the capture missed.
        var client = new ScriptedCodexRunnerClient { RemainingTranscriptFailures = 1 };
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing please", CancellationToken.None);
        client.Append(TranscriptKinds.AssistantText, "the first-turn reply from the rollout");
        client.Append(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeTrue();
        turn.ResponseText.ShouldBe("the first-turn reply from the rollout");
        client.RemainingTranscriptFailures.ShouldBe(0);
    }

    [Test]
    public async Task A_transient_fetch_failure_on_a_later_turn_preserves_the_baseline_instead_of_replaying_the_previous_reply()
    {
        var client = new ScriptedCodexRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 1_500);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("the first prompt of some length", CancellationToken.None);
        client.Append(TranscriptKinds.AssistantText, "reply one — must not leak into turn two");
        client.Append(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var first = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        first.ResponseText.ShouldBe("reply one — must not leak into turn two");

        // Turn 2: the capture fetch is the one that fails. Without CARD-0113 the floor resets to
        // 0, turn 1's TurnEnd satisfies the query, and the adapter reports turn 2 complete with
        // turn 1's text before any new row exists.
        client.RemainingTranscriptFailures = 1;
        await adapter.SendPromptAsync("the brand new prompt", CancellationToken.None);

        var stale = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        stale.TurnCompleted.ShouldBeFalse(
            "a missed capture on turn 2+ must keep the last-known floor; the previous TurnEnd is history");
        stale.ResponseText.ShouldNotBe("reply one — must not leak into turn two");

        client.Append(TranscriptKinds.AssistantText, "reply two — the real answer");
        client.Append(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var second = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        second.TurnCompleted.ShouldBeTrue();
        second.ResponseText.ShouldBe("reply two — the real answer");
    }

    [Test]
    public async Task A_session_that_never_visibly_works_reports_the_turn_incomplete_instead_of_scraping_the_status_bar()
    {
        var client = new ScriptedCodexRunnerClient
        {
            ThrowOnTranscript = true,   // no rollout at all — the stranded shape creates no file
            IndicatorScreenReads = 0,   // …and the TUI never renders the Working line
        };
        // 300ms of quiet inside a 3s window: bare quiet would have fired many times over.
        var adapter = NewAdapter(client, doneQuietMs: 300, doneMaxWaitMs: 3_000, submitConfirmMs: 100);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("the prompt that never submitted", CancellationToken.None);
        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeFalse(
            "quiet without the measured Working-indicator lifecycle is not a completed turn — this "
            + "is the CARD-0108 shape that returned the status bar as the model's answer");
    }

    [Test]
    public async Task The_screen_fallback_completes_when_the_working_indicator_appeared_and_then_went()
    {
        var client = new ScriptedCodexRunnerClient
        {
            ThrowOnTranscript = true, // transcript unavailable: fallback territory
            IndicatorScreenReads = 6, // the turn visibly runs for a few polls, then finishes
        };
        var adapter = NewAdapter(client, doneQuietMs: 300, doneMaxWaitMs: 15_000, submitConfirmMs: 100);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing please", CancellationToken.None);
        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeTrue(
            "the indicator was seen and then left the screen, which is Codex's only positive "
            + "screen-level done signal");
        turn.RawSnapshot.ShouldNotBeNullOrWhiteSpace();
    }

    private static RunnerCodexAdapter NewAdapter(
        ISessionRunnerClient client,
        int doneQuietMs,
        int doneMaxWaitMs,
        int submitConfirmMs = 5_000) =>
        new(
            client,
            Options.Create(new AgentRegistrySettings
            {
                CodexDoneQuietPeriodMs = doneQuietMs,
                CodexDoneMaxWaitMs = doneMaxWaitMs,
                // The submit half is RunnerCodexAdapterSubmitConfirmTests' subject; here it only
                // has to get out of the way quickly.
                CodexSubmitReEnterIntervalMs = 200,
                CodexSubmitAttempts = 0,
                CodexSubmitConfirmTimeoutMs = submitConfirmMs,
            }));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "codex",
        Kind: AgentKind.Codex,
        Exe: "codex.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid());

    private static SessionRunnerTranscriptEvent Row(
        Guid sessionId, long seq, string kind, string? text = null, string? stopReason = null) =>
        new(
            sessionId, seq, kind, $"uuid-{seq}", null, DateTimeOffset.UtcNow, null, text,
            null, null, null, null, stopReason);
}

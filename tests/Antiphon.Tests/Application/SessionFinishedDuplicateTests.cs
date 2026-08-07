using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// One agent start must produce ONE "Agent finished" notification, not one per historic turn.
///
/// Live miss 2026-08-06: starting an agent fired 13 <c>SessionFinished</c> broadcasts inside one
/// second (server log 20:37:18.642 → 20:37:19.565), then 2 more for the single real turn that
/// followed. Two independent causes, both reproduced here through the REAL
/// <see cref="AgentSessionRuntime.ObserveTranscriptAsync"/> ingestion path:
///
/// 1. REPLAY — <c>TranscriptTailer</c> always re-reads the transcript from offset 0, so every
///    historic <c>TurnEnd</c> is re-published on agent start, runner adoption, and /clear
///    fork-follow. The session's JSONL held 12 historic <c>end_turn</c> records.
/// 2. SPLIT API RESPONSE — Claude Code writes one API response as several JSONL records (thinking,
///    then text) and stamps EVERY one with the response's <c>stop_reason</c>, so one turn emits
///    several <c>TurnEnd</c> entries that share a <c>message.id</c>. Real shape: this repo's own
///    session transcript, lines 498/499, both <c>stop_reason=end_turn</c>, both
///    <c>msg_011Cdn4R62je2R7oqpvufLkZ</c>.
///
/// The duplicates were never cosmetic: each one also re-ran the queue flush, typing the next
/// queued message into a session that had not actually reached a new idle point.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionFinishedDuplicateTests
{
    private static Task<BridgeQueueHarness> CreateHarnessAsync() => BridgeQueueHarness.CreateAsync();

    private static int FinishedCount(BridgeQueueHarness h) =>
        h.EventBus.PublishedEvents.Count(e => e.EventName == "SessionFinished");

    private static SessionRunnerTranscriptEvent Entry(
        Guid sessionId,
        long sequence,
        string kind,
        string uuid,
        string? text = null,
        string? stopReason = null,
        string? apiCallId = null) =>
        new(sessionId, sequence, kind, uuid, null, DateTimeOffset.UtcNow, "assistant",
            text, null, null, null, null, stopReason, apiCallId);

    /// <summary>
    /// One complete turn as the tailer emits it: the user's prompt, then the two JSONL records of
    /// the assistant's single API response — a thinking record and a text record, each normalized
    /// into its content part plus a <c>TurnEnd</c> carrying the shared stop_reason and message id.
    /// </summary>
    private static SessionRunnerTranscriptEvent[] Turn(Guid sessionId, long from, string tag, string apiCallId) =>
    [
        Entry(sessionId, from, TranscriptKinds.UserPrompt, $"{tag}-prompt", text: "do the thing"),
        Entry(sessionId, from + 1, TranscriptKinds.Thinking, $"{tag}-think", text: "considering"),
        Entry(sessionId, from + 2, TranscriptKinds.TurnEnd, $"{tag}-think",
            stopReason: "end_turn", apiCallId: apiCallId),
        Entry(sessionId, from + 3, TranscriptKinds.AssistantText, $"{tag}-text", text: "done"),
        Entry(sessionId, from + 4, TranscriptKinds.TurnEnd, $"{tag}-text",
            stopReason: "end_turn", apiCallId: apiCallId),
    ];

    private static async Task ObserveAsync(BridgeQueueHarness h, IEnumerable<SessionRunnerTranscriptEvent> entries)
    {
        foreach (var entry in entries)
            await h.Runtime.ObserveTranscriptAsync(entry, CancellationToken.None);
    }

    [Test]
    public async Task Split_api_response_announces_finished_once()
    {
        await using var h = await CreateHarnessAsync();

        await ObserveAsync(h, Turn(h.SessionId, 1, "t1", "msg_A"));

        FinishedCount(h).ShouldBe(
            1, "the thinking and text records are one API response, so one turn end");
    }

    [Test]
    public async Task Replayed_history_does_not_re_announce_finished()
    {
        await using var h = await CreateHarnessAsync();
        var turn1 = Turn(h.SessionId, 1, "t1", "msg_A");
        var turn2 = Turn(h.SessionId, 6, "t2", "msg_B");

        await ObserveAsync(h, turn1);
        await ObserveAsync(h, turn2);
        FinishedCount(h).ShouldBe(2, "two distinct API responses are two real turn ends");

        // Agent start / runner adoption: a FRESH tailer generation re-reads the same file from
        // offset 0 and republishes everything, renumbering sequences from 1 as it goes. Only the
        // transcript uuids survive the renumbering — which is exactly what the dedup keys on.
        await ObserveAsync(h, Turn(h.SessionId, 1, "t1", "msg_A"));
        await ObserveAsync(h, Turn(h.SessionId, 6, "t2", "msg_B"));

        FinishedCount(h).ShouldBe(2, "replayed history is not news — no toast per historic turn");
    }

    [Test]
    public async Task Interrupt_marker_ends_the_turn_once_and_never_again_on_replay()
    {
        await using var h = await CreateHarnessAsync();
        // An aborted turn writes NO TurnEnd — the "[Request interrupted" user marker IS the
        // boundary (2026-07-29). It must still fire exactly once, and never again on replay.
        var aborted = new[]
        {
            Entry(h.SessionId, 1, TranscriptKinds.UserPrompt, "i-prompt", text: "go"),
            Entry(h.SessionId, 2, TranscriptKinds.ToolCall, "i-tool"),
            Entry(h.SessionId, 3, TranscriptKinds.UserPrompt, "i-mark",
                text: TranscriptKinds.InterruptedPromptPrefix + " by user for tool use]"),
        };

        await ObserveAsync(h, aborted);
        FinishedCount(h).ShouldBe(1, "an interrupted turn is still a turn end");

        await ObserveAsync(h, aborted);
        FinishedCount(h).ShouldBe(1, "a replayed interrupt marker is not a new turn end");
    }

    [Test]
    public async Task Replayed_history_does_not_re_flush_the_queue()
    {
        await using var h = await CreateHarnessAsync();
        await h.SeedPendingMessageAsync("first");
        await h.SeedPendingMessageAsync("second");
        var turn = Turn(h.SessionId, 1, "t1", "msg_A");

        await ObserveAsync(h, turn);
        h.Adapter.SubmittedBodies.ShouldBe(["first"], "one turn end delivers one queued message");

        // The damaging half of the bug: each replayed turn end typed the NEXT queued message into
        // a session that had not reached a new idle point.
        await ObserveAsync(h, turn);

        h.Adapter.SubmittedBodies.ShouldBe(
            ["first"], "replayed history must not type the next queued message into a live session");
    }

    [Test]
    public async Task A_genuinely_new_turn_still_announces_finished()
    {
        await using var h = await CreateHarnessAsync();

        await ObserveAsync(h, Turn(h.SessionId, 1, "t1", "msg_A"));
        // Replay of turn 1 arrives interleaved (adoption), then a brand-new turn happens.
        await ObserveAsync(h, Turn(h.SessionId, 1, "t1", "msg_A"));
        await ObserveAsync(h, Turn(h.SessionId, 6, "t2", "msg_B"));

        FinishedCount(h).ShouldBe(
            2, "suppressing replays must not suppress the next real turn end");
    }
}

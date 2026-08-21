using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// The runner-side working/idle judgement must mirror the server's IsWorkingAsync (and the
/// client's isWorking()) — and stay CONSERVATIVE for the CPU watchdog: "proven idle" requires a
/// completed turn with nothing counting as activity after it. Anything unprovable (empty
/// transcript, activity with no end yet) must read as not-idle so the watchdog never kills a
/// session it cannot judge.
/// </summary>
public class TranscriptWorkingStateTests
{
    [Test]
    public void Empty_transcript_is_not_proven_idle()
    {
        TranscriptWorkingState.IsProvenIdle([]).ShouldBeFalse();
    }

    [Test]
    public void Completed_turn_with_no_later_activity_is_idle()
    {
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.UserPrompt, "do the thing"),
            Evt(2, TranscriptKinds.AssistantText, "done"),
            Evt(3, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
        ]).ShouldBeTrue();
    }

    [Test]
    public void Activity_after_the_turn_end_means_working()
    {
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.UserPrompt, "do the thing"),
            Evt(2, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(3, TranscriptKinds.UserPrompt, "and now this"),
        ]).ShouldBeFalse();
    }

    [Test]
    public void Activity_with_no_completed_turn_is_not_proven_idle()
    {
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.UserPrompt, "do the thing"),
            Evt(2, TranscriptKinds.Thinking, "hmm"),
        ]).ShouldBeFalse();
    }

    [Test]
    public void Interrupt_marker_counts_as_the_turns_end()
    {
        // An aborted turn writes NO TurnEnd — the "[Request interrupted..." user marker IS the end
        // (same rule as the server; live miss 2026-07-29).
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.UserPrompt, "do the thing"),
            Evt(2, TranscriptKinds.Thinking, "hmm"),
            Evt(3, TranscriptKinds.UserPrompt, "[Request interrupted by user]"),
        ]).ShouldBeTrue();
    }

    [Test]
    public void Housekeeping_after_the_turn_end_stays_idle()
    {
        // Turn titles, compaction boundaries and local slash-command records are neither activity
        // nor ends (same exclusions as the server; live misses 2026-07-31).
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.UserPrompt, "do the thing"),
            Evt(2, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(3, TranscriptKinds.TurnTitle, "Doing the thing"),
            Evt(4, TranscriptKinds.CompactBoundary, "Context compacted (auto)"),
            Evt(5, TranscriptKinds.UserPrompt, "<command-name>/model</command-name>"),
            Evt(6, TranscriptKinds.UserPrompt, "<local-command-stdout>Set model</local-command-stdout>"),
        ]).ShouldBeTrue();
    }

    [Test]
    public void Queued_user_prompt_after_a_turn_end_stays_idle()
    {
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(2, TranscriptKinds.QueuedUserPrompt, "a completion note accepted into the composer queue"),
        ]).ShouldBeTrue("queued_command confirms delivery only; it is never a new turn");
    }

    // ---- CARD-0041, in FILE order (which is what this judgement always sees) ------------------
    // The tailer's mirror follows the JSONL: raw typed prompt, boundary, continuation, wrapper,
    // stdout. There is no timestamp override here — deliberately, since file order is truthful —
    // so both halves of the fix have to carry their own weight.
    private const string Continuation =
        "This session is being continued from a previous conversation that ran out of context. "
        + "The conversation is summarized below:";

    [Test]
    public void Manual_compaction_after_a_turn_is_proven_idle()
    {
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.UserPrompt, "the real work"),
            Evt(2, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(3, TranscriptKinds.UserPrompt, "/compact This session is being handed NEW work"),
            Evt(4, TranscriptKinds.CompactBoundary, "Context compacted (manual)"),
            Evt(5, TranscriptKinds.UserPrompt, Continuation),
            Evt(6, TranscriptKinds.UserPrompt, "<command-name>/compact</command-name>"),
            Evt(7, TranscriptKinds.UserPrompt, "<local-command-stdout>Compacted</local-command-stdout>"),
        ]).ShouldBeTrue("a manual /compact runs between turns — the boundary is the previous turn's end");
    }

    [Test]
    public void Continuation_prompt_after_a_manual_boundary_is_not_activity()
    {
        // The exclusion, alone: in file order the continuation lands AFTER the boundary, so
        // boundary-as-end cannot rescue this one (and no timestamp override exists to).
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(2, TranscriptKinds.CompactBoundary, "Context compacted (manual)"),
            Evt(3, TranscriptKinds.UserPrompt, Continuation),
        ]).ShouldBeTrue();
    }

    [Test]
    public void Raw_slash_prefixed_prompt_with_no_boundary_is_not_idle()
    {
        // Matching raw "/"-prefixed text was rejected: a real prompt may start with a slash.
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(2, TranscriptKinds.UserPrompt, "/compact keep the API contract notes"),
        ]).ShouldBeFalse();
    }

    [Test]
    public void Auto_compaction_boundary_mid_turn_is_not_proven_idle()
    {
        // Auto-compaction fires when a request starts over the context threshold — mid-turn. The
        // CPU watchdog reads this verdict: "cannot prove idle" must never come back as idle.
        TranscriptWorkingState.IsProvenIdle(
        [
            Evt(1, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Evt(2, TranscriptKinds.UserPrompt, "now do the big thing"),
            Evt(3, TranscriptKinds.CompactBoundary, "Context compacted (auto)"),
            Evt(4, TranscriptKinds.UserPrompt, Continuation),
        ]).ShouldBeFalse();
    }

    private static RunnerTranscriptEvent Evt(long seq, string kind, string? text = null, string? stopReason = null) =>
        new(
            Guid.Empty, seq, kind,
            Uuid: null, ParentUuid: null, Timestamp: null, Role: null,
            Text: text, ToolName: null, ToolInput: null, ToolUseId: null, ToolIsError: null,
            StopReason: stopReason);
}

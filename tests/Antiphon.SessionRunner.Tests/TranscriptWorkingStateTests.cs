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

    private static RunnerTranscriptEvent Evt(long seq, string kind, string? text = null, string? stopReason = null) =>
        new(
            Guid.Empty, seq, kind,
            Uuid: null, ParentUuid: null, Timestamp: null, Role: null,
            Text: text, ToolName: null, ToolInput: null, ToolUseId: null, ToolIsError: null,
            StopReason: stopReason);
}

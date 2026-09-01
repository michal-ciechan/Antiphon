using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Runner-side working/idle judgement over a tailed transcript. Mirrors the server's
/// SessionMessageQueueService.IsWorkingAsync (and the client's isWorking()) — the session is
/// working while activity outranks the last turn end, where an interrupt marker and a MANUAL
/// compaction boundary count as ENDS (neither an aborted turn nor a compaction writes a TurnEnd)
/// and local slash-command records, turn titles, auto compaction boundaries and compaction's own
/// continuation prompt are housekeeping, not activity. Keep the three implementations in lockstep — with
/// one deliberate divergence: the server/client add a timestamp override because THEIR sequences
/// are arrival-ordered and a catch-up sync can reorder them (2026-08-08); this judgement runs over
/// the tailer's own mirror, which is always in transcript-file order, so sequences alone are
/// truthful here.
/// </summary>
public static class TranscriptWorkingState
{
    public enum WorkingVerdict { Unknown, Working, Idle }

    /// <summary>
    /// True when the transcript proves the session is sitting idle at the prompt: at least one
    /// turn has ENDED (TurnEnd or interrupt marker), and nothing counting as activity has been
    /// written since. Deliberately conservative for the CPU watchdog's sake: an empty transcript,
    /// or one with activity but no end yet, returns false — "cannot prove idle" must never read
    /// as idle, or a legitimately hot session (startup, resume history load) could be killed.
    /// </summary>
    public static bool IsProvenIdle(IReadOnlyList<RunnerTranscriptEvent> entries)
        => Classify(entries) == WorkingVerdict.Idle;

    /// <summary>
    /// CARD-0163: the Herdr label reads this same file-ordered judgement as the CPU watchdog.
    /// Unknown is deliberate: absent evidence must never be presented as a confident idle state.
    /// </summary>
    public static WorkingVerdict Classify(IReadOnlyList<RunnerTranscriptEvent> entries)
    {
        long lastEnd = 0;
        long lastActivity = 0;
        foreach (var entry in entries)
        {
            if (entry.Kind == TranscriptKinds.TurnEnd
                || entry.Kind == TranscriptKinds.SessionRestartBoundary
                || TranscriptKinds.IsManualCompactBoundary(entry.Kind, entry.Text)
                || TranscriptKinds.IsInterruptPrompt(entry.Kind, entry.Text))
            {
                lastEnd = Math.Max(lastEnd, entry.Sequence);
                continue;
            }

            // CompactBoundary here is only ever the AUTO/trigger-less kind — the manual one ranked
            // as an end above. There is no timestamp override to fall back on in this judgement, so
            // the continuation prompt MUST be excluded explicitly: in FILE order it lands after the
            // boundary and would outrank it (CARD-0041).
            if (entry.Kind == TranscriptKinds.TurnTitle
                || entry.Kind == TranscriptKinds.CompactBoundary
                // queued_command carries the time it entered the composer, not its file-order
                // position. It confirms delivery only and must not create a turn boundary.
                || entry.Kind == TranscriptKinds.QueuedUserPrompt
                // The queue-operation housekeeping rows (CARD-0292 S3): same enqueue-time
                // timestamp trap, and an enqueue can be a wedged modal swallowing input.
                || entry.Kind == TranscriptKinds.QueueEnqueue
                || entry.Kind == TranscriptKinds.QueueDequeue
                || entry.Kind == TranscriptKinds.QueueRemove
                || TranscriptKinds.IsCompactionContinuationPrompt(entry.Kind, entry.Text)
                || TranscriptKinds.IsLocalCommandRecord(entry.Kind, entry.Text))
            {
                continue; // housekeeping — neither activity nor an end
            }

            lastActivity = Math.Max(lastActivity, entry.Sequence);
        }

        if (lastEnd == 0 && lastActivity == 0)
            return WorkingVerdict.Unknown;
        return lastEnd > 0 && lastActivity <= lastEnd ? WorkingVerdict.Idle : WorkingVerdict.Working;
    }
}

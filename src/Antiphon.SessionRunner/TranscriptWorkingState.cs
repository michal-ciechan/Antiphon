using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Runner-side working/idle judgement over a tailed transcript. Mirrors the server's
/// SessionMessageQueueService.IsWorkingAsync (and the client's isWorking()) — the session is
/// working while activity outranks the last turn end, where an interrupt marker counts as an END
/// (an aborted turn writes no TurnEnd) and local slash-command records, turn titles and compaction
/// boundaries are housekeeping, not activity. Keep the three implementations in lockstep.
/// </summary>
public static class TranscriptWorkingState
{
    /// <summary>
    /// True when the transcript proves the session is sitting idle at the prompt: at least one
    /// turn has ENDED (TurnEnd or interrupt marker), and nothing counting as activity has been
    /// written since. Deliberately conservative for the CPU watchdog's sake: an empty transcript,
    /// or one with activity but no end yet, returns false — "cannot prove idle" must never read
    /// as idle, or a legitimately hot session (startup, resume history load) could be killed.
    /// </summary>
    public static bool IsProvenIdle(IReadOnlyList<RunnerTranscriptEvent> entries)
    {
        long lastEnd = 0;
        long lastActivity = 0;
        foreach (var entry in entries)
        {
            if (entry.Kind == TranscriptKinds.TurnEnd
                || TranscriptKinds.IsInterruptPrompt(entry.Kind, entry.Text))
            {
                lastEnd = Math.Max(lastEnd, entry.Sequence);
                continue;
            }

            if (entry.Kind == TranscriptKinds.TurnTitle
                || entry.Kind == TranscriptKinds.CompactBoundary
                || TranscriptKinds.IsLocalCommandRecord(entry.Kind, entry.Text))
            {
                continue; // housekeeping — neither activity nor an end
            }

            lastActivity = Math.Max(lastActivity, entry.Sequence);
        }

        return lastEnd > 0 && lastActivity <= lastEnd;
    }
}

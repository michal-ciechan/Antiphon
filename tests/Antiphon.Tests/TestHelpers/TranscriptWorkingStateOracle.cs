using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

// Frozen pre-CARD-0698 LINQ. Keep independent of the production SQL helper.
internal static class TranscriptWorkingStateOracle
{
    internal static async Task<IReadOnlyDictionary<Guid, bool>> ReadAsync(
        AppDbContext db, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0)
            return new Dictionary<Guid, bool>();

        // Mirror the client's isWorking(): the agent is working while activity outranks the last
        // turn-end. An interrupt marker ("[Request interrupted...") counts as a turn END, not
        // activity — an aborted turn writes NO TurnEnd, and counting the marker as activity left
        // the session permanently "working" and stranded every WhenIdle delivery (2026-07-29).
        // A SessionRestartBoundary is a turn end for the same reason: the relaunch proved the old
        // turn's process is gone (2026-08-08). A MANUAL compaction boundary is one too: /compact
        // runs only between turns, and no TurnEnd is ever coming for it (2026-08-11, CARD-0041 —
        // a compacted session read "working" for two days because TWO post-compaction records
        // escaped the exclusions below: the RAW typed "/compact …" prompt, which Claude records in
        // addition to the <command-name> wrapper, and the synthetic continuation prompt. Both are
        // outranked once the boundary itself is the turn's end; the continuation is excluded from
        // activity as well, because it lands AFTER the boundary). An AUTO boundary stays
        // housekeeping — it fires mid-turn, so counting it as an end would read a working session
        // as idle. Predicates inlined for EF translation, like the interrupt prefix.
        var end = db.TranscriptEntries
            .Where(t => sessionIds.Contains(t.AgentSessionId)
                && (t.Kind == TranscriptKinds.TurnEnd
                    || t.Kind == TranscriptKinds.SessionRestartBoundary
                    || (t.Kind == TranscriptKinds.CompactBoundary
                        && t.Text != null
                        && t.Text.Contains(TranscriptKinds.ManualCompactMarker))
                    || (t.Kind == TranscriptKinds.UserPrompt
                        && t.Text != null
                        && t.Text.StartsWith(TranscriptKinds.InterruptedPromptPrefix))))
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { SessionId = g.Key, Seq = g.Max(t => t.Sequence), Ts = g.Max(t => t.Timestamp) });
        var activity = db.TranscriptEntries
            .Where(t => sessionIds.Contains(t.AgentSessionId)
                && t.Kind != TranscriptKinds.TurnEnd
                && t.Kind != TranscriptKinds.TurnTitle
                && t.Kind != TranscriptKinds.SessionRestartBoundary
                // queued_command carries its composer enqueue timestamp, which can be older than
                // preceding file-order records. It confirms delivery only; treating it as activity
                // could make the timestamp override report a busy session idle.
                && t.Kind != TranscriptKinds.QueuedUserPrompt
                // The queue-operation housekeeping rows (CARD-0292 S3) share the same timestamp
                // trap — enqueue time predates file-order predecessors — and prove nothing about
                // work: an enqueue can be a wedged modal swallowing input.
                && t.Kind != TranscriptKinds.QueueEnqueue
                && t.Kind != TranscriptKinds.QueueDequeue
                && t.Kind != TranscriptKinds.QueueRemove
                // Local slash-command records (/model, /status …) are housekeeping with NO
                // TurnEnd — counting them as activity stranded WhenIdle deliveries (2026-07-31).
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && (t.Text.StartsWith(TranscriptKinds.LocalCommandPrefix)
                        || t.Text.StartsWith(TranscriptKinds.LocalCommandStdoutPrefix)))
                // Compaction is idle-time housekeeping, not work: counting the boundary as
                // activity would flip an idle session to permanently "working" (no TurnEnd ever
                // follows), stranding every WhenIdle message — including the recovery note. The
                // blanket exclusion stays: manual boundaries are ranked as ENDS above, and
                // auto/trigger-less ones are neither activity nor an end.
                && t.Kind != TranscriptKinds.CompactBoundary
                // The synthetic "This session is being continued from a previous conversation…"
                // record compaction writes: nobody typed it and no TurnEnd follows (CARD-0041).
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && t.Text.StartsWith(TranscriptKinds.CompactionContinuationPromptPrefix))
                && !(t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && t.Text.StartsWith(TranscriptKinds.InterruptedPromptPrefix)));

        var workingAfterEnd = await (
            from activityEntry in activity
            join sessionEnd in end on activityEntry.AgentSessionId equals sessionEnd.SessionId
            // Stored sequences are ARRIVAL-ordered: catch-up can rebase stale pre-gap activity
            // above a persisted TurnEnd. The timestamp decision must therefore be row-correlated,
            // not a comparison of group maxima: working requires ONE post-end activity whose own
            // timestamp does not prove it predates that end. Null timestamps preserve the prior
            // conservative rule (they cannot prove stale).
            where activityEntry.Sequence > sessionEnd.Seq
                && (activityEntry.Timestamp == null
                    || sessionEnd.Ts == null
                    || activityEntry.Timestamp >= sessionEnd.Ts)
            select activityEntry.AgentSessionId)
            .Distinct()
            .ToListAsync(ct);

        // Sessions without an end have always read working when they contain activity. Keep that
        // rule in SQL too; splitting it from the inner join above avoids EF's untranslatable left
        // join over the grouped end projection.
        var workingWithoutEnd = await activity
            .Where(t => !end.Select(e => e.SessionId).Contains(t.AgentSessionId))
            .Select(t => t.AgentSessionId)
            .Distinct()
            .ToListAsync(ct);

        var working = workingAfterEnd.Concat(workingWithoutEnd).ToHashSet();
        return sessionIds.ToDictionary(sessionId => sessionId, working.Contains);
    }

}

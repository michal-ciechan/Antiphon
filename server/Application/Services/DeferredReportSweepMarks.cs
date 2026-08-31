namespace Antiphon.Server.Application.Services;

/// <summary>
/// In-memory per-session watermark for the deferred-report sweep (CARD-0248). The sweep's
/// predicates are monotonic, so without this it re-enters settlement every PollIntervalSeconds
/// tick for the life of an affected task — the re-entry channel that ate the CARD-0159 nudge.
/// Correctness never depends on this (settlement's own gates make re-entry inert); it bounds
/// the query load. A changed boundary always hands off immediately.
///
/// <para>Lives outside <see cref="AgentTaskDispatcher"/> for the same reason
/// <see cref="DeadSessionFirstSeenState"/> does: the dispatcher is scoped and a fresh instance
/// is built for every 5 s tick, so a map held there would never suppress anything. A server
/// restart drops the map — worst case one redundant hand-off per session, absorbed by the
/// gates.</para>
/// </summary>
public sealed class DeferredReportSweepMarks
{
    private readonly Dictionary<Guid, SweepMark> _marks = new();
    private readonly object _gate = new();

    private readonly record struct SweepMark(
        long BoundarySequence,
        DateTime? LastEntryAt,
        DateTime LastHandOffUtc);

    /// <summary>
    /// Whether this arm should hand the session to settlement. <paramref name="rehandSeconds"/>
    /// &lt;= 0 restores per-tick re-handing (tests).
    /// </summary>
    public bool ShouldHandOff(
        Guid sessionId, long boundarySequence, DateTime? lastEntryAt, DateTime now, int rehandSeconds)
    {
        if (rehandSeconds <= 0)
            return true;

        lock (_gate)
        {
            if (!_marks.TryGetValue(sessionId, out var mark))
                return true;
            var keyChanged = mark.BoundarySequence != boundarySequence
                || mark.LastEntryAt != lastEntryAt;
            if (keyChanged)
                return true;
            return now - mark.LastHandOffUtc >= TimeSpan.FromSeconds(rehandSeconds);
        }
    }

    public void RecordHandOff(
        Guid sessionId, long boundarySequence, DateTime? lastEntryAt, DateTime now)
    {
        lock (_gate)
        {
            _marks[sessionId] = new SweepMark(boundarySequence, lastEntryAt, now);
        }
    }

    /// <summary>Drop keys not in this tick's session list so the map self-cleans as tasks settle.</summary>
    public void Prune(IReadOnlyCollection<Guid> liveSessionIds)
    {
        lock (_gate)
        {
            if (_marks.Count == 0)
                return;
            var live = liveSessionIds as HashSet<Guid> ?? liveSessionIds.ToHashSet();
            var stale = _marks.Keys.Where(id => !live.Contains(id)).ToList();
            foreach (var id in stale)
                _marks.Remove(id);
        }
    }
}

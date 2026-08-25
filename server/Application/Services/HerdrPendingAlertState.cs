using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton first-seen / last-raised clock for reconciliation pass 1b (CARD-0186 S3).
/// <see cref="SessionReconciliationService"/> is scoped — a fresh instance per sweep — so a
/// dictionary held there would forget the first observation every 15 seconds and never fire.
/// In-memory on purpose: a restart is a fresh start.
/// </summary>
public sealed class HerdrPendingAlertState
{
    private readonly ConcurrentDictionary<Guid, DateTime> _firstSeen = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastRaised = new();

    /// <summary>Records the first observation of this pending session; returns that timestamp.</summary>
    public DateTime Observe(Guid sessionId, DateTime nowUtc) =>
        _firstSeen.GetOrAdd(sessionId, nowUtc);

    /// <summary>
    /// True once the session has been pending longer than <paramref name="alertAfter"/> and has
    /// not been raised inside <paramref name="dedupWindow"/>. The incident is a timeline row;
    /// this gate is the only thing that stops a 15-second sweep from writing 240 rows an hour.
    /// </summary>
    public bool TryRaise(Guid sessionId, DateTime nowUtc, TimeSpan alertAfter, TimeSpan dedupWindow)
    {
        var first = Observe(sessionId, nowUtc);
        if (nowUtc - first < alertAfter)
            return false;
        if (_lastRaised.TryGetValue(sessionId, out var last) && nowUtc - last < dedupWindow)
            return false;

        _lastRaised[sessionId] = nowUtc;
        return true;
    }

    /// <summary>
    /// Pending cleared (herdr came back, or the session exited). The next pending episode is news
    /// again after another alert-after wait. The incident row is not deleted.
    /// </summary>
    public void Clear(Guid sessionId)
    {
        _firstSeen.TryRemove(sessionId, out _);
        _lastRaised.TryRemove(sessionId, out _);
    }

    /// <summary>Drop tracking for any session no longer reporting Pending.</summary>
    public void RetainOnly(IReadOnlySet<Guid> stillPending)
    {
        foreach (var id in _firstSeen.Keys.ToArray())
        {
            if (!stillPending.Contains(id))
                Clear(id);
        }
    }
}

using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>One party refused the repository mutation lease and still waiting for it (CARD-0672 D-2).</summary>
public sealed record RepositoryLeaseWaiter(Guid TaskId, string Purpose, DateTimeOffset Since);

/// <summary>
/// CARD-0672 D-2: the dispatch-first turnstile at land admission, kept outside the lease primitive.
/// A queued task the dispatcher could not claim because the lease was busy registers here, keyed by
/// the repository's common directory; a land about to acquire the lease yields while any waiter is
/// registered, for a bounded time, so the dispatcher's next tick takes the lease in the gap the
/// never-sleeping land queue would otherwise close.
///
/// <para>In memory by design, like <see cref="RunnerSyncLeaseWaits"/> and the lease's own owner
/// registry: a restart forgets every waiter and the next dispatcher tick re-registers the ones that
/// are still refused. A second server process is the lease's existing "unknown owner" case.</para>
/// </summary>
public sealed class RepositoryLeaseWaiters
{
    // Same rule as RepositoryMutationLease's owner key: full path, trailing separator trimmed,
    // case-insensitive on Windows only.
    private static readonly StringComparer KeyComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    // A land request whose budget ran out keeps its first-yield entry so later re-picks do not
    // yield again; once it is admitted nothing reads it, so entries this old are dropped.
    private static readonly TimeSpan YieldRetention = TimeSpan.FromDays(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<Guid, RepositoryLeaseWaiter>> _byCommon = new(KeyComparer);
    private readonly Dictionary<Guid, YieldBudget> _yields = new();

    /// <summary>The registry key for a common directory.</summary>
    public static string KeyFor(string commonDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDirectory));

    /// <summary>
    /// Records that <paramref name="taskId"/> waits for the lease on <paramref name="commonDirectory"/>.
    /// A repeat registration with the same purpose keeps its original <see cref="RepositoryLeaseWaiter.Since"/>.
    /// </summary>
    public void Register(string commonDirectory, Guid taskId, string purpose, DateTimeOffset now)
    {
        var key = KeyFor(commonDirectory);
        lock (_gate)
        {
            foreach (var (otherKey, waiters) in _byCommon)
            {
                if (!KeyComparer.Equals(otherKey, key))
                    waiters.Remove(taskId);
            }

            RemoveEmpty();
            if (!_byCommon.TryGetValue(key, out var entries))
                _byCommon[key] = entries = new Dictionary<Guid, RepositoryLeaseWaiter>();
            if (entries.TryGetValue(taskId, out var existing) && existing.Purpose == purpose)
                return;
            entries[taskId] = new RepositoryLeaseWaiter(taskId, purpose, now);
        }
    }

    /// <summary>The task no longer waits, on any repository.</summary>
    public void Clear(Guid taskId)
    {
        lock (_gate)
        {
            foreach (var waiters in _byCommon.Values)
                waiters.Remove(taskId);
            RemoveEmpty();
        }
    }

    /// <summary>
    /// End of a dispatcher tick: every <c>dispatch</c> waiter not refused the lease on this tick
    /// (dispatched, failed, cancelled, held for another reason, or no longer queued) is dropped.
    /// Waiters with any other purpose are left to their owners.
    /// </summary>
    public void ReconcileDispatch(ISet<Guid> stillHeld)
    {
        lock (_gate)
        {
            foreach (var waiters in _byCommon.Values)
            {
                var stale = waiters.Values
                    .Where(w => w.Purpose == RepositoryLeasePurposes.Dispatch && !stillHeld.Contains(w.TaskId))
                    .Select(w => w.TaskId)
                    .ToList();
                foreach (var id in stale)
                    waiters.Remove(id);
            }

            RemoveEmpty();
        }
    }

    /// <summary>The waiters on one repository, oldest first.</summary>
    public IReadOnlyList<RepositoryLeaseWaiter> Snapshot(string commonDirectory)
    {
        var key = KeyFor(commonDirectory);
        lock (_gate)
        {
            return _byCommon.TryGetValue(key, out var waiters)
                ? waiters.Values.OrderBy(w => w.Since).ThenBy(w => w.TaskId).ToList()
                : [];
        }
    }

    /// <summary>Whether anything waits on this repository.</summary>
    public bool Any(string commonDirectory)
    {
        var key = KeyFor(commonDirectory);
        lock (_gate)
            return _byCommon.TryGetValue(key, out var waiters) && waiters.Count > 0;
    }

    /// <summary>Whether anything waits on any repository; lets a land skip resolving its common directory.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
                return _byCommon.Count == 0;
        }
    }

    /// <summary>
    /// When <paramref name="requestId"/> first yielded in its current budget: <paramref name="now"/>
    /// when it has not yielded yet. Stable until <see cref="EndYield"/>.
    /// </summary>
    public DateTimeOffset FirstYield(Guid requestId, DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var old in _yields.Where(y => now - y.Value.First > YieldRetention).Select(y => y.Key).ToList())
                _yields.Remove(old);
            if (_yields.TryGetValue(requestId, out var budget))
                return budget.First;
            _yields[requestId] = new YieldBudget(now, false);
            return now;
        }
    }

    /// <summary>
    /// True exactly once per budget: the first time the caller reports that it is proceeding past an
    /// exhausted budget, so its warning is written once and not on every re-pick.
    /// </summary>
    public bool MarkExhausted(Guid requestId)
    {
        lock (_gate)
        {
            if (!_yields.TryGetValue(requestId, out var budget) || budget.Exhausted)
                return false;
            _yields[requestId] = budget with { Exhausted = true };
            return true;
        }
    }

    /// <summary>The request no longer yields; a later yield starts a fresh budget.</summary>
    public void EndYield(Guid requestId)
    {
        lock (_gate)
            _yields.Remove(requestId);
    }

    private void RemoveEmpty()
    {
        foreach (var empty in _byCommon.Where(p => p.Value.Count == 0).Select(p => p.Key).ToList())
            _byCommon.Remove(empty);
    }

    private sealed record YieldBudget(DateTimeOffset First, bool Exhausted);
}

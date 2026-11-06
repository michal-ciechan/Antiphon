using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>One party refused the repository mutation lease and still waiting for it (CARD-0672 D-2).</summary>
public sealed record RepositoryLeaseWaiter(Guid TaskId, string Purpose, DateTimeOffset Since,
    bool VerificationCompatible = false);

/// <summary>CARD-1427 D-8: what a land about to acquire the lease does with the waiters it sees.</summary>
public enum LandYieldOutcome
{
    /// <summary>No waiter on this repository (or the yield is disabled): acquire as usual.</summary>
    Proceed,
    /// <summary>Waiters are registered and the budget has time left: stand aside, Held.</summary>
    Yield,
    /// <summary>Waiters are registered but the request's budget is spent: acquire anyway.</summary>
    ProceedExhausted,
}

/// <summary>
/// CARD-1427 D-8: the pure part of the land's admission yield. <paramref name="Waiters"/> is the
/// repository's waiter list the decision saw (oldest first); <paramref name="FirstYield"/> is when
/// this request's budget began (<c>now</c> when it has not yielded); <paramref name="FirstExhaustion"/>
/// is true on the one <see cref="LandYieldOutcome.ProceedExhausted"/> that should write the Warning.
/// </summary>
public sealed record LandYieldDecision(LandYieldOutcome Outcome, IReadOnlyList<RepositoryLeaseWaiter> Waiters,
    DateTimeOffset FirstYield, bool FirstExhaustion);

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
public sealed class RepositoryLeaseWaiters(IOptions<DelegationSettings>? settings = null, TimeProvider? clock = null)
{
    // Same rule as RepositoryMutationLease's owner key: full path, trailing separator trimmed,
    // case-insensitive on Windows only.
    private static readonly StringComparer KeyComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    // A land request whose budget ran out keeps its first-yield entry until the land service ends
    // it (admission or a terminal return), so the re-picks in between do not yield again; an
    // entry no boundary ever ended is dropped at this age.
    private static readonly TimeSpan YieldRetention = TimeSpan.FromDays(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<Guid, RepositoryLeaseWaiter>> _byCommon = new(KeyComparer);
    private readonly Dictionary<Guid, YieldBudget> _yields = new();
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SignalReady(bool force = false)
    {
        lock (_gate)
            if (force || _byCommon.Count != 0) _ready.TrySetResult();
    }

    public Task WaitForReadyAsync(CancellationToken ct)
    {
        lock (_gate) return _ready.Task.WaitAsync(ct);
    }

    public bool TryTakeReady(out IReadOnlyList<Guid> tasks)
    {
        lock (_gate)
        {
            tasks = [];
            if (!_ready.Task.IsCompleted) return false;
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            tasks = _byCommon.Values.SelectMany(w => w.Values)
                .Where(w => w.Purpose == RepositoryLeasePurposes.Dispatch && w.VerificationCompatible)
                .OrderBy(w => w.Since).ThenBy(w => w.TaskId.ToString("N"), StringComparer.Ordinal)
                .Select(w => w.TaskId).Distinct().Take(8).ToArray();
            return true;
        }
    }

    public bool AllowsPreflightTurn(string common, Guid identity)
    {
        var cap = settings?.Value.LandYieldToDispatchMaxSeconds ?? 90;
        if (cap <= 0) return true;
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var first = Snapshot(common).FirstOrDefault(w => w.VerificationCompatible
            && now - w.Since < TimeSpan.FromSeconds(w.Purpose == RepositoryLeasePurposes.StartRefFetch ? 30 : cap));
        return first is null || first.TaskId == identity;
    }

    /// <summary>The registry key for a common directory.</summary>
    public static string KeyFor(string commonDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDirectory));

    /// <summary>
    /// Records that <paramref name="taskId"/> waits for the lease on <paramref name="commonDirectory"/>.
    /// A repeat registration with the same purpose keeps its original <see cref="RepositoryLeaseWaiter.Since"/>.
    /// </summary>
    public void Register(string commonDirectory, Guid taskId, string purpose, DateTimeOffset now,
        bool verificationCompatible = false)
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
            {
                entries[taskId] = existing with { VerificationCompatible = verificationCompatible };
                return;
            }
            entries[taskId] = new RepositoryLeaseWaiter(taskId, purpose, now, verificationCompatible);
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
    /// <para>CARD-1427 D-1: a registered dispatch waiter whose task is in
    /// <paramref name="prerequisitePending"/> (its only hold this tick is a prerequisite of the cut:
    /// the same-card inspection is pending, or the observation went stale at claim) is kept, with
    /// its original <see cref="RepositoryLeaseWaiter.Since"/>. Membership never registers a waiter;
    /// only a lease refusal does.</para>
    /// </summary>
    public void ReconcileDispatch(ISet<Guid> stillHeld, IReadOnlyList<Guid>? considered = null,
        IReadOnlySet<Guid>? prerequisitePending = null)
    {
        lock (_gate)
        {
            foreach (var waiters in _byCommon.Values)
            {
                var stale = waiters.Values
                    .Where(w => w.Purpose == RepositoryLeasePurposes.Dispatch && !stillHeld.Contains(w.TaskId)
                        && (prerequisitePending is null || !prerequisitePending.Contains(w.TaskId))
                        && (considered is null || considered.Contains(w.TaskId)))
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
    /// CARD-1427 D-8 (CARD-0672 D-2): the land admission's yield decision, without its database
    /// writes. <see cref="LandYieldOutcome.Proceed"/> ends the request's budget (no waiter on this
    /// repository, or the yield is disabled). <see cref="LandYieldOutcome.Yield"/> carries the
    /// waiters and the budget's first yield, which the Held row reports as its start.
    /// <see cref="LandYieldOutcome.ProceedExhausted"/> is the budget spent; its
    /// <see cref="LandYieldDecision.FirstExhaustion"/> is true exactly once per budget so the land
    /// writes its one Warning. The budget is never reset by waiter churn; only
    /// <see cref="EndYield"/> (admission or a terminal return) ends it.
    /// </summary>
    public LandYieldDecision DecideYield(string commonDirectory, Guid requestId, DateTimeOffset now, int maxSeconds)
    {
        if (maxSeconds <= 0)
        {
            EndYield(requestId);
            return new LandYieldDecision(LandYieldOutcome.Proceed, [], now, false);
        }

        var waiters = Snapshot(commonDirectory);
        if (waiters.Count == 0)
        {
            EndYield(requestId);
            return new LandYieldDecision(LandYieldOutcome.Proceed, waiters, now, false);
        }

        var first = FirstYield(requestId, now);
        if (now - first >= TimeSpan.FromSeconds(maxSeconds))
            return new LandYieldDecision(LandYieldOutcome.ProceedExhausted, waiters, first, MarkExhausted(requestId));
        return new LandYieldDecision(LandYieldOutcome.Yield, waiters, first, false);
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

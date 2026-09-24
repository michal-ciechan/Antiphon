using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0657 R4. When each runner-bound task's settlement sync first found its repository lease
/// busy, keyed by task id. A settlement attempt runs inside one dispatcher sweep and waits only a
/// slice; this is what lets the next sweep's attempt continue the SAME wait, so the task blocks
/// lease-busy when the cumulative wait reaches <c>Delegation:RunnerSyncBudgetSeconds</c>.
///
/// In memory by design: no task column or settlement evidence records an unsettled attempt, and a
/// server restart only restarts the wait (one more budget), never skips it. An entry is removed as
/// soon as an attempt ends any other way than still waiting.
/// </summary>
public sealed class RunnerSyncLeaseWaits
{
    /// <summary>The one map this server process uses; every sweep's scope builds its own sync service.</summary>
    public static RunnerSyncLeaseWaits Process { get; } = new();

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _firstBusy = new();

    /// <summary>When the task's current wait began: <paramref name="now"/> if none is running.</summary>
    public DateTimeOffset FirstBusy(Guid taskId, DateTimeOffset now) => _firstBusy.GetOrAdd(taskId, now);

    /// <summary>The task's wait, if any, is over.</summary>
    public void End(Guid taskId) => _firstBusy.TryRemove(taskId, out _);
}

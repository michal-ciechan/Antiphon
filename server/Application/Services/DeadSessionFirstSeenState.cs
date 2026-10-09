using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton first-seen clock for the dispatcher's dead-session sweep (CARD-0021): when did each
/// open task FIRST look like its session had died?
///
/// <para>It lives outside <see cref="AgentTaskDispatcher"/> for the same reason
/// <see cref="SessionReAdoptionState"/> lives outside the reconciler — the dispatcher is scoped and
/// a fresh instance is built for every 5 s tick, so a map held there would restart the grace on
/// every pass and the sweep would never fire at all (a silent disarm, which is worse than none).
/// </para>
///
/// <para>In memory on purpose. The grace exists so a WRONG "Failed" row has time to be re-adopted
/// by reconciliation before a task is failed under it; a server restart just means the task is
/// observed afresh and waits the window again, which only ever delays a failure and never skips
/// one.</para>
/// </summary>
public sealed class DeadSessionFirstSeenState
{
    private readonly ConcurrentDictionary<Guid, long> _firstSeen = new();

    /// <summary>
    /// Records the <see cref="TimeProvider"/> timestamp from the first sweep that saw this task
    /// dead and returns that stamp. A later call returns the same stamp and does not replace it.
    /// </summary>
    public long Observe(Guid taskId, long timestamp) => _firstSeen.GetOrAdd(taskId, timestamp);

    /// <summary>
    /// Forget a task: it stopped looking dead (re-adopted, settled, canceled) or it has now been
    /// failed. Without this a session that recovered would keep the grace it had already burned and
    /// a later, unrelated death would be acted on instantly.
    /// </summary>
    public void Forget(Guid taskId) => _firstSeen.TryRemove(taskId, out _);

    /// <summary>Is this task currently being timed? (Diagnostics and tests.)</summary>
    public bool IsTracking(Guid taskId) => _firstSeen.ContainsKey(taskId);
}

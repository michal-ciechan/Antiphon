using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton skip-set for CARD-0299 S2: while a boot-wedge relaunch is in flight the old
/// session is Stopping/Stopped and <see cref="AgentTaskDispatcher.FailDeadSessionTasksAsync"/>
/// must not Fail the task this tick. Same reason <see cref="DeadSessionFirstSeenState"/> is a
/// singleton — the dispatcher is scoped.
/// </summary>
public sealed class BootWedgeRelaunchState
{
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public void Mark(Guid taskId) => _pending[taskId] = 0;

    public void Forget(Guid taskId) => _pending.TryRemove(taskId, out _);

    public bool IsPending(Guid taskId) => _pending.ContainsKey(taskId);
}

using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The hand-off between the dispatcher's check sweep and the worker that actually runs a check
/// (CARD-0047 §1.1).
///
/// <para>It exists for one reason: <c>AgentTaskDispatcher.TickAsync</c> is SERIAL and runs every
/// 5 s, while a check takes seconds — and, once the interpreter lands, a model call. Awaiting one
/// inside the tick would stall dispatching for its duration, so the sweep only claims due tasks and
/// drops their ids here; <c>AgentTaskCheckHostedService</c> drains them on its own thread. Same
/// shape, and the same reason, as <see cref="AgentSessionLaunchQueue"/>.</para>
///
/// <para>Unbounded on purpose. The producer is a sweep that has already claimed each task by
/// advancing its <c>NextCheckAt</c>, so it cannot queue the same task twice on consecutive ticks,
/// and a bounded channel would have to either block the tick (the thing this exists to prevent) or
/// silently drop a check that has already been counted against the task's budget.</para>
/// </summary>
public sealed class AgentTaskCheckQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>Hand a claimed task to the worker. Never blocks.</summary>
    public bool TryEnqueue(Guid taskId) => _channel.Writer.TryWrite(taskId);

    /// <summary>Claimed task ids, in arrival order, until the token is cancelled.</summary>
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    /// <summary>How many claims are waiting — for tests, and for the tick's own debug logging.</summary>
    public int PendingCount => _channel.Reader.Count;

    /// <summary>Take one claim without waiting; false when the queue is empty (tests).</summary>
    public bool TryDequeue(out Guid taskId) => _channel.Reader.TryRead(out taskId);
}

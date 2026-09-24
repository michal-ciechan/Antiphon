using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// In-process hand-off for explicit branch landings (CARD-0331). The durable fact that a land
/// is wanted lives on <c>AgentTasks.LandRequestedAt</c>; this channel exists so the sweep never
/// waits on git. The active set is the honest answer to "is a land queued or running here".
/// </summary>
public sealed class AgentTaskLandQueue
{
    private readonly ConcurrentDictionary<Guid, byte> _active = new();
    private readonly Channel<LandRequest> _channel = Channel.CreateUnbounded<LandRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>
    /// Hand a pending land to the drain. Returns false when this id is already queued or
    /// running in this process — never duplicated.
    /// </summary>
    public bool TryEnqueue(Guid taskId, string? verifyFilter, Guid? requestId = null)
    {
        if (!_active.TryAdd(taskId, 0))
            return false;
        if (_channel.Writer.TryWrite(new LandRequest(taskId, verifyFilter, requestId)))
            return true;
        _active.TryRemove(taskId, out _);
        return false;
    }

    /// <summary>Queued or running in this process, now.</summary>
    public bool IsActive(Guid taskId) => _active.ContainsKey(taskId);

    /// <summary>The drain calls this in a finally after every <c>RunAsync</c>.</summary>
    public void Release(Guid taskId) => _active.TryRemove(taskId, out _);

    public IAsyncEnumerable<LandRequest> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// Ids queued or running in this process. Unbounded SingleReader channels do not
    /// implement <c>Reader.Count</c>; the active set is the durable in-process fact.
    /// </summary>
    public int PendingCount => _active.Count;

    /// <summary>Take one claim without waiting; false when the queue is empty (tests).</summary>
    public bool TryDequeue(out LandRequest request) => _channel.Reader.TryRead(out request!);

    /// <summary>
    /// Point-in-time view of the single global worker. Empty until the queue records
    /// accepted channel entries; a new instance has no executing owner.
    /// </summary>
    public LandQueueSnapshot Capture(DateTime observedAt) =>
        new(observedAt, null, Array.Empty<LandQueueEntry>());

    public sealed record LandRequest(Guid TaskId, string? VerifyFilter, Guid? RequestId = null);
}

/// <summary>One accepted channel entry. EntryId distinguishes a requeue from an unread older item.</summary>
public sealed record LandQueueEntry(long EntryId, Guid TaskId, Guid? RequestId);

/// <summary>Executing entry plus waiting entries in channel order. Position is one-based among waiting entries only.</summary>
public sealed record LandQueueSnapshot(DateTime ObservedAt, LandQueueEntry? Executing, IReadOnlyList<LandQueueEntry> Waiting)
{
    public int WaitingCount => Waiting.Count;

    public int? WaitingPosition(Guid taskId, Guid? requestId)
    {
        for (var i = 0; i < Waiting.Count; i++)
        {
            if (Matches(Waiting[i], taskId, requestId))
                return i + 1;
        }

        return null;
    }

    public bool IsExecuting(Guid taskId, Guid? requestId) =>
        Executing is not null && Matches(Executing, taskId, requestId);

    private static bool Matches(LandQueueEntry entry, Guid taskId, Guid? requestId)
    {
        if (requestId is Guid id)
            return entry.RequestId == id;
        return entry.TaskId == taskId && entry.RequestId is null;
    }
}

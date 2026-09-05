using System.Threading.Channels;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One distillation request handed from settlement to the serial drainer (CARD-0330 D2). Same
/// shape as <see cref="AgentTaskCheckQueue"/>: unbounded so the producer never blocks, single
/// reader because the seat answers one turn at a time.
/// </summary>
public sealed class OutputDistillationQueue
{
    private readonly Channel<DistillRequest> _channel = Channel.CreateUnbounded<DistillRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>Hand a request to the worker. Never blocks.</summary>
    public bool TryEnqueue(DistillRequest request) => _channel.Writer.TryWrite(request);

    /// <summary>Requests in arrival order, until the token is cancelled.</summary>
    public IAsyncEnumerable<DistillRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>How many requests are waiting — for tests.</summary>
    public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <summary>Take one request without waiting; false when the queue is empty (tests).</summary>
    public bool TryDequeue(out DistillRequest request) => _channel.Reader.TryRead(out request!);
}

/// <summary>One distillation job. <see cref="QueuedMessageId"/> is the completion note to improve.</summary>
public sealed record DistillRequest(Guid TaskId, Guid? QueuedMessageId);

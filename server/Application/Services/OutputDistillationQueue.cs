using System.Threading.Channels;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One distillation request handed from settlement to the serial drainer (CARD-0330 D2).
/// Bounded admission uses TryWrite (never waits and never silently drops accepted requests).
/// </summary>
public sealed class OutputDistillationQueue
{
    private readonly Channel<DistillRequest> _channel;

    public OutputDistillationQueue(IOptions<DelegationSettings>? settings = null)
    {
        _channel = Channel.CreateBounded<DistillRequest>(new BoundedChannelOptions(
            Math.Max(1, settings?.Value.OutputDistillerQueueCapacity ?? 3))
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
        });
    }

    private int _closed;
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public void Complete()
    {
        Interlocked.Exchange(ref _closed, 1);
        _channel.Writer.TryComplete();
    }

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
public sealed record DistillRequest(
    Guid TaskId, Guid? QueuedMessageId,
    DateTimeOffset RequestedAt, DateTimeOffset DeadlineAt, OutputDistillerMode Mode);

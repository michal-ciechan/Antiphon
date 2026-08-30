using Antiphon.Messaging;

namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Durable first-seen marker for an accepted inbound record (CARD-0245 S2). Lives in the
/// gateway's Inbox, not on SessionQueuedMessage.
/// </summary>
public sealed record InboundReceipt
{
    public required Guid Id { get; init; }
    public required string Channel { get; init; }
    public required string ChannelMessageId { get; init; }
    public required string ConversationId { get; init; }
    public required string ReplyHandle { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public required string Topic { get; init; }
    public required int Partition { get; init; }
    public required long Offset { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
    public DateTimeOffset? OperationalEventPublishedAt { get; init; }
    public DateTimeOffset? NextAckAttemptAt { get; init; }
    public int AckAttemptCount { get; init; }
}

public interface IInboxReceiptStore
{
    Task<IReadOnlyList<InboundReceipt>> GetOverdueAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);

    Task MarkAcknowledgedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken);

    Task MarkOperationalEventPublishedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken);

    Task ScheduleAckRetryAsync(Guid id, DateTimeOffset nextAttemptAt, int attemptCount, CancellationToken cancellationToken);
}

/// <summary>Called after a successful inbound Kafka produce so the receipt store has the offset.</summary>
public interface IInboundReceiptSink
{
    Task RecordAsync(ChannelMessage message, string envelopeJson, string topic, int partition, long offset, CancellationToken cancellationToken);
}

public interface IInboundUnconsumedEventPublisher
{
    Task PublishAsync(InboundUnconsumedEvent evt, CancellationToken cancellationToken);
}

public interface IAppHostHealthProbe
{
    /// <summary>Diagnostic string only. Never the lag verdict.</summary>
    Task<string> ProbeAsync(CancellationToken cancellationToken);
}

namespace Antiphon.Messaging;

/// <summary>
/// Operational event on <c>channels.ops.inbound-unconsumed</c> (CARD-0245 S2). Published by the
/// gateway when an accepted inbound record has not been consumed by the Antiphon consumer group
/// within the inbound-unconsumed budget. Additive shared wire type; never a synthetic
/// <see cref="ChannelMessage"/> on <c>channels.inbound</c>.
/// </summary>
public sealed record InboundUnconsumedEvent
{
    public required string Provider { get; init; }

    public required string ConversationId { get; init; }

    /// <summary>The channel-native id of the original inbound message.</summary>
    public required string OriginalMessageId { get; init; }

    /// <summary>Gateway/Slack clock: <see cref="ChannelMessage.Timestamp"/> at first receipt.</summary>
    public required DateTimeOffset FirstSeenAt { get; init; }

    public required string Topic { get; init; }

    public required int Partition { get; init; }

    public required long Offset { get; init; }

    public required DateTimeOffset DetectedAt { get; init; }

    public required bool Acknowledged { get; init; }

    public string? AcknowledgementError { get; init; }

    /// <summary>AppHost HTTP health probe, diagnostics only — never the lag verdict.</summary>
    public string? AppHostHealth { get; init; }
}

namespace Antiphon.Messaging.Service;

public enum InboxStatus
{
    Pending,
    Answered,
    Ignored,
}

/// <summary>
/// A received channel message, stored so the API can list "things to reply to".
/// Flat columns are for querying; <see cref="EnvelopeJson"/> holds the full canonical
/// <c>ChannelMessage</c> (including the raw native payload).
/// </summary>
public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = "";
    public string ChannelMessageId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string? ConversationTitle { get; set; }
    public string? AuthorDisplay { get; set; }
    public string? Text { get; set; }
    public bool MentionsMe { get; set; }
    public bool HasAttachments { get; set; }
    public string ReplyHandle { get; set; } = "";
    public InboxStatus Status { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public string EnvelopeJson { get; set; } = "";

    /// <summary>Kafka topic the envelope was consumed from (CARD-0245 S2).</summary>
    public string? Topic { get; set; }

    public int? Partition { get; set; }

    public long? Offset { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public DateTimeOffset? OperationalEventPublishedAt { get; set; }

    public DateTimeOffset? NextAckAttemptAt { get; set; }

    public int AckAttemptCount { get; set; }
}

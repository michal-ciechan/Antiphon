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
}

using System.Text.Json;

namespace Antiphon.Messaging;

/// <summary>
/// A normalized inbound message from any channel (Telegram, WhatsApp, Teams, ...).
/// Common features are surfaced as first-class properties; the complete native payload
/// is preserved verbatim in <see cref="Raw"/> so no channel-specific fidelity is lost.
/// </summary>
public sealed record ChannelMessage
{
    /// <summary>Our own stable id for this message (ULID/GUID), unique across all channels.</summary>
    public required string Id { get; init; }

    /// <summary>Stable channel key, e.g. "telegram", "whatsapp", "teams".</summary>
    public required string Channel { get; init; }

    /// <summary>The channel-native message id.</summary>
    public required string ChannelMessageId { get; init; }

    public required Conversation Conversation { get; init; }

    public required Participant Author { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Normalized plain text of the message, if any.</summary>
    public string? Text { get; init; }

    public IReadOnlyList<Mention> Mentions { get; init; } = [];

    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    /// <summary>The message this one replies to / quotes, if any.</summary>
    public ReplyReference? ReplyTo { get; init; }

    /// <summary>Opaque token carrying everything an adapter needs to address a reply back to this conversation.</summary>
    public required string ReplyHandle { get; init; }

    /// <summary>The full channel-native payload, preserved verbatim (e.g. a Telegram <c>Update</c>).</summary>
    public required JsonElement Raw { get; init; }
}

public sealed record Conversation
{
    public required string Id { get; init; }
    public required ConversationKind Kind { get; init; }
    public string? Title { get; init; }
}

public enum ConversationKind
{
    Direct,
    Group,
    Channel,
}

public sealed record Participant
{
    public required string Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Username { get; init; }

    /// <summary>True when the author is our own bot/account.</summary>
    public bool IsSelf { get; init; }
}

public sealed record Mention
{
    public required string Id { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>True when this mention targets our own bot/account.</summary>
    public bool IsMe { get; init; }
}

public sealed record Attachment
{
    public required AttachmentKind Kind { get; init; }
    public string? Name { get; init; }
    public string? Mime { get; init; }
    public long? Size { get; init; }

    /// <summary>Channel-native reference used to fetch the bytes (e.g. a Telegram file_id).</summary>
    public required string ChannelRef { get; init; }

    /// <summary>Direct URL to the content, when the channel exposes one.</summary>
    public string? Url { get; init; }
}

public enum AttachmentKind
{
    Image,
    Video,
    Audio,
    Voice,
    File,
    Sticker,
    Location,
    Contact,
    Other,
}

public sealed record ReplyReference
{
    public required string ChannelMessageId { get; init; }
    public string? Excerpt { get; init; }
}

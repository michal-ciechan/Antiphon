using System.Text.Json;

namespace Antiphon.Messaging;

/// <summary>
/// A normalized outbound reply. An adapter denormalizes it into the channel's native send call.
/// Provide either <see cref="ReplyHandle"/> (from the inbound message) or <see cref="ConversationId"/>.
/// </summary>
public sealed record ChannelReply
{
    public required string Channel { get; init; }

    /// <summary>
    /// What this reply IS: a final answer (default), an interim progress note while the agent is still
    /// working, or a question the agent needs answered before it can continue. Adapters may render the
    /// kinds differently (e.g. a progress prefix, a question marker); consumers can filter on it.
    /// Serialized as a camelCase string; absent in older payloads deserializes to <see cref="ChannelReplyKind.Answer"/>.
    /// </summary>
    public ChannelReplyKind Kind { get; init; } = ChannelReplyKind.Answer;

    /// <summary>Opaque handle copied from the inbound <see cref="ChannelMessage.ReplyHandle"/>.</summary>
    public string? ReplyHandle { get; init; }

    /// <summary>Target conversation id (alternative to <see cref="ReplyHandle"/>).</summary>
    public string? ConversationId { get; init; }

    /// <summary>If set, sent as a reply to / quote of this channel-native message id.</summary>
    public string? ReplyToMessageId { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<OutboundAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Raw channel-specific fields merged into the native send call (e.g. Telegram <c>parse_mode</c>,
    /// <c>disable_notification</c>). Kept raw on purpose so full channel features stay reachable.
    /// </summary>
    public JsonElement? RawOverrides { get; init; }
}

/// <summary>See <see cref="ChannelReply.Kind"/>. Order matters for JSON back-compat: Answer is the default.</summary>
public enum ChannelReplyKind
{
    /// <summary>The agent's final output for the message it was answering.</summary>
    Answer,

    /// <summary>An interim "still working" note emitted mid-task — not the final output.</summary>
    Progress,

    /// <summary>The agent is blocked on a question and needs a human reply to continue.</summary>
    Question,
}

public sealed record OutboundAttachment
{
    public required AttachmentKind Kind { get; init; }

    /// <summary>A channel ref to re-send, a URL, or a path the adapter can read bytes from.</summary>
    public required string Source { get; init; }

    public string? Name { get; init; }
    public string? Caption { get; init; }
}

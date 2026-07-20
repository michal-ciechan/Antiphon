using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// An external conversation we participate in — a Telegram DM/group today; WhatsApp, Discord, etc.
/// later. Rows are discovered (upserted) from inbound bridge messages, keyed by
/// (<see cref="Provider"/>, <see cref="ExternalId"/>). Binding <see cref="AgentId"/> routes the
/// channel's messages to that agent's session; the agent's turn output flows back down the channel.
/// </summary>
public class ChatChannel
{
    public Guid Id { get; set; }

    /// <summary>Stable channel provider key, e.g. "telegram", "whatsapp", "discord".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider-native conversation id (e.g. a Telegram chat id).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public ChatChannelKind Kind { get; set; }

    /// <summary>Conversation title (group name), when the provider exposes one.</summary>
    public string? Title { get; set; }

    /// <summary>The agent this channel routes to. Null = unmapped (messages are recorded, not routed).</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Routing on/off without losing the agent binding.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set, this channel is an ALERT SINK: operational alerts with severity ≥ this value are
    /// delivered here (throttled/grouped). Null = not an alert sink.
    /// </summary>
    public AlertSeverity? AlertMinSeverity { get; set; }

    /// <summary>Latest opaque reply-routing token from the provider (addresses outbound sends).</summary>
    public string? ReplyHandle { get; set; }

    /// <summary>Provider-native id of the last recorded message — dedupes Kafka's at-least-once redelivery.</summary>
    public string? LastChannelMessageId { get; set; }

    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public string? LastAuthor { get; set; }
    public long MessageCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Agent? Agent { get; set; }
}

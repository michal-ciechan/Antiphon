namespace Antiphon.Server.Domain.Entities;

/// <summary>Durable native inbound identity and complete envelope until a queue row owns it.</summary>
public sealed class ChannelInbound
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string NativeMessageId { get; set; } = string.Empty;
    public string? EnvelopeJson { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ChatChannelId { get; set; }
    public Guid? QueueMessageId { get; set; }
    /// <summary>Database assigned FIFO order; timestamps and GUIDs may tie or sort differently.</summary>
    public long AcceptanceSequence { get; set; }
    public DateTime AcceptedAt { get; set; }
    public DateTime? TransferredAt { get; set; }
    public DateTime? WakeTimeoutIncidentAt { get; set; }
}

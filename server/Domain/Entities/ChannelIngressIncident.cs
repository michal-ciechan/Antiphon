namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Agent-independent record of an inbound message the Antiphon consumer group never consumed
/// within the gateway's budget (CARD-0245 S2). No required <c>AgentId</c>: the event can arrive
/// before the restarted bridge has catalogued the channel.
/// </summary>
public class ChannelIngressIncident
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string OriginalMessageId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public bool Acknowledged { get; set; }
    public string? AcknowledgementError { get; set; }
    public string? AppHostHealth { get; set; }
    public DateTime CreatedAt { get; set; }
}

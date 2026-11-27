namespace Antiphon.Messaging.Service;

/// <summary>One provider invocation per durable Kafka record. Retains only addressing and receipt evidence.</summary>
public sealed class GatewayOutboundOperation
{
    public string Id { get; set; } = "";
    public string Channel { get; set; } = "";
    public string? ConversationId { get; set; }
    public string AnsweredMessageIdsJson { get; set; } = "[]";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? ProviderMessageId { get; set; }
}

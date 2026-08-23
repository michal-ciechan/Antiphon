namespace Antiphon.Messaging.Gateway;

/// <summary>Settings for a channel gateway talking to the Antiphon messaging bus over Kafka.</summary>
public sealed class AntiphonGatewayOptions
{
    public const string SectionName = "AntiphonGateway";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string InboundTopic { get; set; } = "channels.inbound";
    public string OutboundTopic { get; set; } = "channels.outbound";

    /// <summary>
    /// Consumer group for this gateway process. REQUIRED to be unique per gateway process so
    /// two gateways do not steal each other's outbound replies.
    /// </summary>
    public string ConsumerGroup { get; set; } = "antiphon-gateway";

    /// <summary>
    /// Maximum Kafka message size in bytes, producer and consumer side. 20 MB — the bus-wide cap
    /// so attachment payloads fit; must match the topics' <c>max.message.bytes</c>.
    /// </summary>
    public int MaxMessageBytes { get; set; } = 20 * 1024 * 1024;

    public KafkaSecurityOptions Security { get; set; } = new();

    public TopicLayout TopicLayout { get; set; } = TopicLayout.Shared;

    public TimeSpan IngressRestartBackoff { get; set; } = TimeSpan.FromSeconds(5);

    public string ResolveInboundTopic() => Resolve(InboundTopic);

    public string ResolveOutboundTopic() => Resolve(OutboundTopic);

    private string Resolve(string sharedName) => TopicLayout switch
    {
        TopicLayout.Shared => sharedName,
        TopicLayout.PerProvider => throw new NotSupportedException(
            "TopicLayout.PerProvider is a future follow-up (Tier 2 per-provider topics) and is not implemented."),
        _ => throw new NotSupportedException($"Unknown TopicLayout '{TopicLayout}'."),
    };
}

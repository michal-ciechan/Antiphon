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

    /// <summary>
    /// Outbound consumer auto-offset-reset. Production gateways keep <c>Earliest</c> so a
    /// new group does not skip pending replies. The fake gateway uses <c>Latest</c> so a
    /// restart does not replay the outbound topic into <c>/deliveries</c>.
    /// </summary>
    public string AutoOffsetReset { get; set; } = "Earliest";

    /// <summary>
    /// Antiphon server's inbound consumer group. Distinct from this gateway's own
    /// <see cref="ConsumerGroup"/>. Lag is measured against this group (CARD-0245 S2).
    /// </summary>
    public string AntiphonConsumerGroup { get; set; } = "antiphon-consumer";

    /// <summary>Additive operational topic for inbound-unconsumed events. Not channels.inbound.</summary>
    public string InboundUnconsumedTopic { get; set; } = "channels.ops.inbound-unconsumed";

    /// <summary>How old an inbox receipt must be before lag is a proven overdue (default 5 minutes).</summary>
    public int InboundUnconsumedMinutes { get; set; } = 5;

    /// <summary>How often the inbound-unconsumed monitor runs.</summary>
    public int InboundUnconsumedPollSeconds { get; set; } = 60;

    /// <summary>AppHost /health URL, diagnostics only — never the lag verdict.</summary>
    public string AppHostHealthUrl { get; set; } = "http://localhost:17202/health";

    /// <summary>Detection-only monitor. Never restarts AppHost.</summary>
    public bool InboundUnconsumedMonitorEnabled { get; set; } = true;

    public string ResolveInboundTopic() => Resolve(InboundTopic);

    public string ResolveOutboundTopic() => Resolve(OutboundTopic);

    public string ResolveInboundUnconsumedTopic() => Resolve(InboundUnconsumedTopic);

    private string Resolve(string sharedName) => TopicLayout switch
    {
        TopicLayout.Shared => sharedName,
        TopicLayout.PerProvider => throw new NotSupportedException(
            "TopicLayout.PerProvider is a future follow-up (Tier 2 per-provider topics) and is not implemented."),
        _ => throw new NotSupportedException($"Unknown TopicLayout '{TopicLayout}'."),
    };
}

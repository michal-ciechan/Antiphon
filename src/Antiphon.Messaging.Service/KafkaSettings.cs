namespace Antiphon.Messaging.Service;

/// <summary>Typed Kafka/Redpanda settings (bound via <c>IOptions</c>).</summary>
public sealed class KafkaSettings
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string InboundTopic { get; set; } = "channels.inbound";
    public string OutboundTopic { get; set; } = "channels.outbound";
    public string ConsumerGroup { get; set; } = "antiphon-messaging-service";
}

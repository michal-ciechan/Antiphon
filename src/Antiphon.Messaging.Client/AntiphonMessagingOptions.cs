namespace Antiphon.Messaging.Client;

/// <summary>Settings for a consumer talking to an Antiphon messaging bridge over Kafka.</summary>
public sealed class AntiphonMessagingOptions
{
    public const string SectionName = "AntiphonMessaging";

    public string BootstrapServers { get; set; } = "localhost:19092";
    public string InboundTopic { get; set; } = "channels.inbound";
    public string OutboundTopic { get; set; } = "channels.outbound";

    /// <summary>Consumer group for this app's inbound consumption. MUST be distinct from the bridge's own
    /// group (and any other consumer) so each gets its own copy of the inbound stream.</summary>
    public string ConsumerGroup { get; set; } = "antiphon-consumer";
}

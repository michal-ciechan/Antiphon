namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Kafka client security for a gateway. Today the bus is plaintext behind the Tailscale
/// perimeter; SASL/ACL identity was dropped (CARD-0150 S5) and is not wired here.
/// </summary>
public sealed class KafkaSecurityOptions
{
    public string SecurityProtocol { get; set; } = "Plaintext";
}

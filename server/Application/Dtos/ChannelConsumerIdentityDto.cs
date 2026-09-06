using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Dtos;

public sealed record ChannelConsumerBrokerDto(string Host, int? Port);

/// <summary>Allowlisted effective consumer identity. Constructing this never contacts Kafka.</summary>
public sealed record ChannelConsumerIdentityDto(
    string ConsumerGroup, string InboundTopic, bool Enabled, IReadOnlyList<ChannelConsumerBrokerDto> Brokers)
{
    public static ChannelConsumerIdentityDto From(AntiphonMessagingOptions options, ChannelBridgeSettings bridge) =>
        new(options.ConsumerGroup, options.InboundTopic, bridge.Enabled,
            (options.BootstrapServers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseBroker).ToArray());

    private static ChannelConsumerBrokerDto ParseBroker(string address)
    {
        address = address.Trim();
        var scheme = address.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) address = address[(scheme + 3)..];
        // Credentials and URL suffixes are never part of the address allowlist.
        var credentials = address.LastIndexOf('@');
        if (credentials >= 0) address = address[(credentials + 1)..];
        var suffix = address.IndexOfAny(['/', '?', '#']);
        if (suffix >= 0) address = address[..suffix];
        var colon = address.LastIndexOf(':');
        if (colon < 0 || address.EndsWith(']')) return new(address, null);
        return new(address[..colon], int.TryParse(address[(colon + 1)..], out var port) ? port : null);
    }
}

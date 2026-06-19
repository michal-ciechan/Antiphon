namespace Antiphon.Messaging;

/// <summary>
/// External-I/O seam for a messaging channel (Telegram, WhatsApp, Teams, ...).
/// An adapter owns the channel's credentials and wire protocol: it normalizes inbound
/// traffic to <see cref="ChannelMessage"/> and denormalizes <see cref="ChannelReply"/>
/// into native sends. One implementation per channel.
/// </summary>
public interface IChannelAdapter
{
    /// <summary>Stable channel key, matching <see cref="ChannelMessage.Channel"/> (e.g. "telegram").</summary>
    string Channel { get; }

    /// <summary>What this channel supports.</summary>
    ChannelCapabilities Capabilities { get; }

    /// <summary>
    /// Long-running ingress: receive native updates and yield normalized messages until cancelled.
    /// </summary>
    IAsyncEnumerable<ChannelMessage> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Send a normalized reply through the channel's native API.</summary>
    Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken);
}

/// <summary>Outcome of an adapter send.</summary>
public sealed record SendResult
{
    public required bool Ok { get; init; }

    /// <summary>The channel-native id of the sent message, when the channel returns one.</summary>
    public string? ChannelMessageId { get; init; }

    public string? Error { get; init; }

    public static SendResult Sent(string? channelMessageId = null) =>
        new() { Ok = true, ChannelMessageId = channelMessageId };

    public static SendResult Failed(string error) =>
        new() { Ok = false, Error = error };
}

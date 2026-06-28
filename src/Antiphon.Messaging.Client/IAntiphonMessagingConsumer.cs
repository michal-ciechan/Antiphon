namespace Antiphon.Messaging.Client;

/// <summary>Streams inbound <see cref="ChannelMessage"/>s from the bridge's inbound topic. Enumerate it from a
/// background service; the stream ends when the token is cancelled.</summary>
public interface IAntiphonMessagingConsumer
{
    IAsyncEnumerable<ChannelMessage> ConsumeAsync(CancellationToken cancellationToken = default);
}

namespace Antiphon.Messaging.Client;

/// <summary>Sends outbound messages through the Antiphon bridge by producing a
/// <see cref="ChannelReply"/> to the outbound topic. The bridge delivers it to the target channel.</summary>
public interface IAntiphonMessagingProducer
{
    Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default);
}

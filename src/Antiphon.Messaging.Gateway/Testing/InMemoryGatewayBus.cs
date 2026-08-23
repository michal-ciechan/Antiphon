using System.Threading.Channels;

namespace Antiphon.Messaging.Gateway.Testing;

/// <summary>
/// In-memory stand-in for the two gateway hosted services: observe produced inbound
/// <see cref="ChannelMessage"/>s (keyed by conversation id) and push <see cref="ChannelReply"/>s
/// as if they arrived on the outbound topic. One package, not a fifth, because a gateway author's
/// tests need it and nobody needs it without the Gateway.
/// </summary>
public sealed class InMemoryGatewayBus
{
    private readonly object _gate = new();
    private readonly List<(string Key, ChannelMessage Message)> _produced = [];
    private readonly Channel<ChannelReply> _replies = Channel.CreateUnbounded<ChannelReply>();

    /// <summary>Inbound messages the gateway would have produced, with the Kafka key used.</summary>
    public IReadOnlyList<(string Key, ChannelMessage Message)> ProducedInbound
    {
        get { lock (_gate) return _produced.ToList(); }
    }

    /// <summary>Record an inbound produce. The Kafka key is <see cref="Conversation.Id"/>.</summary>
    public void ProduceInbound(ChannelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
            _produced.Add((message.Conversation.Id, message));
    }

    /// <summary>Push a reply as if it arrived on <c>channels.outbound</c>.</summary>
    public void PushReply(ChannelReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        _replies.Writer.TryWrite(reply);
    }

    public async IAsyncEnumerable<ChannelReply> ConsumeRepliesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var reply in _replies.Reader.ReadAllAsync(cancellationToken))
            yield return reply;
    }

    public void Complete() => _replies.Writer.TryComplete();
}

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Antiphon.Messaging;

namespace Antiphon.Messaging.FakeGateway;

/// <summary>
/// The fake gateway's <see cref="IChannelAdapter"/>: <see cref="ReceiveAsync"/> yields
/// messages injected via <c>POST /inbound</c>, and <see cref="SendAsync"/> records each
/// reply to <see cref="DeliveryStore"/> (honouring <see cref="PauseState"/>).
/// </summary>
public sealed class FakeChannelAdapter : IChannelAdapter
{
    private readonly System.Threading.Channels.Channel<PendingInbound> _inbound =
        System.Threading.Channels.Channel.CreateUnbounded<PendingInbound>();
    private readonly DeliveryStore _store;
    private readonly PauseState _pause;
    private readonly ILogger<FakeChannelAdapter> _logger;

    public FakeChannelAdapter(string channel, DeliveryStore store, PauseState pause, ILogger<FakeChannelAdapter> logger)
    {
        Channel = channel;
        _store = store;
        _pause = pause;
        _logger = logger;
        Capabilities = new ChannelCapabilities { Channel = channel };
    }

    public string Channel { get; }

    public ChannelCapabilities Capabilities { get; }

    /// <summary>
    /// Enqueue <paramref name="message"/> for ingress. Completes after the gateway
    /// ingress pump has taken the yield (i.e. after the Kafka produce).
    /// </summary>
    public async Task InjectAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var produced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inbound.Writer.TryWrite(new PendingInbound(message, produced)))
            throw new InvalidOperationException($"Fake channel '{Channel}' inbound pipe is closed.");

        await produced.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    public async IAsyncEnumerable<ChannelMessage> ReceiveAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var pending in _inbound.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                yield return pending.Message;
            }
            finally
            {
                pending.Produced.TrySetResult();
            }
        }
    }

    public async Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
    {
        while (_pause.Paused)
            await Task.Delay(50, cancellationToken);

        var recorded = _store.Record(reply, DateTime.UtcNow);
        _logger.LogInformation(
            "Recorded delivery #{Seq}: [{Channel}/{Conversation}] {Text}",
            recorded.Seq, recorded.Channel, recorded.ConversationId,
            Truncate(recorded.Text, 120));
        return SendResult.Sent(recorded.Seq.ToString());
    }

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max] + "…";

    private sealed record PendingInbound(ChannelMessage Message, TaskCompletionSource Produced);
}

/// <summary>Lookup of the fake adapters registered in this process, keyed by channel.</summary>
public sealed class FakeChannelHub
{
    private readonly Dictionary<string, FakeChannelAdapter> _byChannel;

    public FakeChannelHub(params FakeChannelAdapter[] adapters)
    {
        _byChannel = adapters.ToDictionary(a => a.Channel, StringComparer.OrdinalIgnoreCase);
        Adapters = adapters;
    }

    public IReadOnlyList<FakeChannelAdapter> Adapters { get; }

    public IReadOnlyCollection<string> Channels => _byChannel.Keys;

    public bool TryGet(string channel, [NotNullWhen(true)] out FakeChannelAdapter? adapter) =>
        _byChannel.TryGetValue(channel, out adapter);
}

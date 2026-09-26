using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Antiphon.Messaging.Client.Testing;

/// <summary>
/// In-memory <see cref="IAntiphonMessagingProducer"/> + <see cref="IAntiphonMessagingConsumer"/> for consumer tests:
/// inject inbound <see cref="ChannelMessage"/>s (as if Telegram delivered them) and assert the
/// <see cref="ChannelReply"/>s the app produced — no Kafka, no Telegram. Mirrors the real client's contract.
/// </summary>
public sealed class FakeAntiphonMessagingClient : IAntiphonMessagingProducer, IAntiphonMessagingConsumer
{
    private static readonly JsonElement EmptyRaw = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly object _gate = new();
    private readonly List<ChannelReply> _sent = [];
    private readonly Channel<ChannelMessage> _inbound = Channel.CreateUnbounded<ChannelMessage>();
    private readonly List<(ChannelMessage Message, bool Acknowledged)> _records = [];
    private readonly Channel<bool> _recordSignal = Channel.CreateUnbounded<bool>();
    private bool _completed;
    private long _messageId = 1000;

    /// <summary>Replies the app produced via <see cref="SendAsync"/>, in order.</summary>
    public IReadOnlyList<ChannelReply> SentReplies
    {
        get { lock (_gate) return _sent.ToList(); }
    }

    public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        lock (_gate) _sent.Add(reply);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ChannelMessage> ConsumeAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _inbound.Reader.ReadAllAsync(cancellationToken))
            yield return message;
    }

    public async IAsyncEnumerable<InboundDelivery> ConsumeDeliveriesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var index = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            ChannelMessage? message = null;
            bool completed;
            lock (_gate)
            {
                while (index < _records.Count && _records[index].Acknowledged)
                    index++;
                if (index < _records.Count)
                    message = _records[index].Message;
                completed = _completed;
            }
            if (message is not null)
            {
                var recordIndex = index++;
                yield return new InboundDelivery(message, null, (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        var record = _records[recordIndex];
                        _records[recordIndex] = (record.Message, true);
                    }
                    return Task.CompletedTask;
                });
                continue;
            }
            if (completed)
                yield break;
            await _recordSignal.Reader.ReadAsync(cancellationToken);
        }
    }

    public int AcknowledgedCount
    {
        get { lock (_gate) return _records.Count(r => r.Acknowledged); }
    }

    /// <summary>Push an arbitrary inbound message.</summary>
    public void InjectInbound(ChannelMessage message)
    {
        lock (_gate) _records.Add((message, false));
        _inbound.Writer.TryWrite(message);
        _recordSignal.Writer.TryWrite(true);
    }

    /// <summary>Build and push a contract-accurate Telegram text message (the common test case).</summary>
    public ChannelMessage InjectTelegramText(
        string chatId,
        string text,
        ConversationKind kind = ConversationKind.Group,
        string? username = null,
        string? authorId = null,
        string? conversationTitle = null)
    {
        var message = new ChannelMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            Channel = "telegram",
            ChannelMessageId = Interlocked.Increment(ref _messageId).ToString(),
            Conversation = new Conversation { Id = chatId, Kind = kind, Title = conversationTitle },
            Author = new Participant { Id = authorId ?? "1001", Username = username, DisplayName = username },
            Timestamp = DateTimeOffset.UtcNow,
            Text = text,
            ReplyHandle = chatId,
            Raw = EmptyRaw,
        };
        InjectInbound(message);
        return message;
    }

    /// <summary>Complete the inbound stream so an in-flight <see cref="ConsumeAsync"/> ends gracefully.</summary>
    public void Complete()
    {
        lock (_gate) _completed = true;
        _inbound.Writer.TryComplete();
        _recordSignal.Writer.TryWrite(true);
    }
}

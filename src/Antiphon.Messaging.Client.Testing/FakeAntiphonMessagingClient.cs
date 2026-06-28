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

    /// <summary>Push an arbitrary inbound message.</summary>
    public void InjectInbound(ChannelMessage message) => _inbound.Writer.TryWrite(message);

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
    public void Complete() => _inbound.Writer.TryComplete();
}

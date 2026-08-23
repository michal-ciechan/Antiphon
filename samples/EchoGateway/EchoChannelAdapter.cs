using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Messaging;

namespace EchoGateway;

/// <summary>
/// Console as a channel. Each non-empty stdin line becomes an inbound
/// <see cref="ChannelMessage"/> on channel <c>echo</c>; each
/// <see cref="ChannelReply"/> is written to stdout.
/// </summary>
public sealed class EchoChannelAdapter : IChannelAdapter
{
    public const string ChannelKey = "echo";
    public const string ConversationId = "echo-console";

    private static readonly JsonElement EmptyRaw = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly TextReader _input;
    private readonly TextWriter _output;

    public EchoChannelAdapter(TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        _input = input;
        _output = output;
        Capabilities = new ChannelCapabilities
        {
            Channel = ChannelKey,
            MaxTextLength = 4000,
        };
    }

    public string Channel => ChannelKey;

    public ChannelCapabilities Capabilities { get; }

    public async IAsyncEnumerable<ChannelMessage> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (line is null)
            {
                // stdin closed. Do not end the enumerable — GatewayIngressService would
                // treat that as a fault and restart the pump.
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { }
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            yield return new ChannelMessage
            {
                Id = Guid.NewGuid().ToString("n"),
                Channel = ChannelKey,
                ChannelMessageId = Guid.NewGuid().ToString("n"),
                Conversation = new Conversation
                {
                    Id = ConversationId,
                    Kind = ConversationKind.Direct,
                    Title = "echo console",
                },
                Author = new Participant
                {
                    Id = "echo-user",
                    DisplayName = "echo-user",
                    // False: this is a human line, not the bot echoing itself.
                    IsSelf = false,
                },
                Timestamp = DateTimeOffset.UtcNow,
                Text = line,
                ReplyHandle = ConversationId,
                Raw = EmptyRaw,
            };
        }
    }

    public async Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reply);

        // Honour ReplyHandle, then ConversationId. A real adapter uses that to address
        // the native conversation; this sample has one stdout.
        _ = reply.ReplyHandle ?? reply.ConversationId;

        var text = reply.Text ?? "";
        if (reply.Kind != ChannelReplyKind.Answer)
            text = $"[{reply.Kind}] {text}";
        if (reply.Attachments.Count > 0)
            text = string.IsNullOrEmpty(text)
                ? $"({reply.Attachments.Count} attachment(s))"
                : $"{text} ({reply.Attachments.Count} attachment(s))";

        if (string.IsNullOrEmpty(text))
            return SendResult.Sent();

        await _output.WriteLineAsync(text.AsMemory(), cancellationToken);
        await _output.FlushAsync(cancellationToken);
        return SendResult.Sent(Guid.NewGuid().ToString("n"));
    }
}

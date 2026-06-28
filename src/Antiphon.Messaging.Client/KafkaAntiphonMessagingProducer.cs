using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Client;

/// <summary>Kafka-backed <see cref="IAntiphonMessagingProducer"/>. Keyed by conversation id for per-chat ordering.</summary>
public sealed class KafkaAntiphonMessagingProducer : IAntiphonMessagingProducer, IDisposable
{
    private readonly AntiphonMessagingOptions _options;
    private readonly IProducer<string, string> _producer;

    public KafkaAntiphonMessagingProducer(IOptions<AntiphonMessagingOptions> options)
    {
        _options = options.Value;
        _producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = _options.BootstrapServers }).Build();
    }

    public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
    {
        var value = JsonSerializer.Serialize(reply, MessagingJson.Options);
        var key = reply.ConversationId ?? reply.ReplyHandle ?? string.Empty;
        await _producer.ProduceAsync(
            _options.OutboundTopic,
            new Message<string, string> { Key = key, Value = value },
            cancellationToken);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

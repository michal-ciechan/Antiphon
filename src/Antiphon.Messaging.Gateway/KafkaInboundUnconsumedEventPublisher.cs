using System.Text.Json;
using Antiphon.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

public sealed class KafkaInboundUnconsumedEventPublisher(
    IProducer<string, string> producer,
    IOptions<AntiphonGatewayOptions> options,
    ILogger<KafkaInboundUnconsumedEventPublisher> logger) : IInboundUnconsumedEventPublisher
{
    private readonly AntiphonGatewayOptions _options = options.Value;

    public async Task PublishAsync(InboundUnconsumedEvent evt, CancellationToken cancellationToken)
    {
        var topic = _options.ResolveInboundUnconsumedTopic();
        var value = JsonSerializer.Serialize(evt, MessagingJson.Options);
        await producer.ProduceAsync(
            topic,
            new Message<string, string> { Key = evt.ConversationId, Value = value },
            cancellationToken);
        logger.LogWarning(
            "[inbound-unconsumed] published {Provider} {Conversation} offset {Topic}/{Partition}:{Offset} ack={Ack}",
            evt.Provider, evt.ConversationId, evt.Topic, evt.Partition, evt.Offset, evt.Acknowledged);
    }
}

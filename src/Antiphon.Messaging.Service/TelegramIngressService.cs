using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Service;

/// <summary>
/// Runs each channel adapter's ingress loop and publishes every normalized <c>ChannelMessage</c>
/// to the inbound topic (keyed by conversation id for per-chat ordering).
/// </summary>
public sealed class TelegramIngressService(
    IEnumerable<IChannelAdapter> adapters,
    IProducer<string, string> producer,
    IOptions<KafkaSettings> kafka,
    JsonSerializerOptions json,
    ILogger<TelegramIngressService> logger) : BackgroundService
{
    private readonly KafkaSettings _kafka = kafka.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(adapters.Select(adapter => PumpAsync(adapter, stoppingToken)));

    private async Task PumpAsync(IChannelAdapter adapter, CancellationToken ct)
    {
        logger.LogInformation("[ingress] starting channel {Channel}", adapter.Channel);
        await foreach (var message in adapter.ReceiveAsync(ct))
        {
            try
            {
                var value = JsonSerializer.Serialize(message, json);
                await producer.ProduceAsync(
                    _kafka.InboundTopic,
                    new Message<string, string> { Key = message.Conversation.Id, Value = value },
                    ct);
                logger.LogInformation("[ingress] {Channel} {Conversation} -> {Topic}",
                    adapter.Channel, message.Conversation.Id, _kafka.InboundTopic);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[ingress] failed to publish {Channel} message", adapter.Channel);
            }
        }
    }
}

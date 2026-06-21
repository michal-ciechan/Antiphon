using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Service;

/// <summary>Consumes the outbound topic and dispatches each reply through the matching channel adapter.</summary>
public sealed class OutboundConsumerService(
    IEnumerable<IChannelAdapter> adapters,
    IOptions<KafkaSettings> kafka,
    JsonSerializerOptions json,
    ILogger<OutboundConsumerService> logger) : BackgroundService
{
    private readonly KafkaSettings _kafka = kafka.Value;
    private readonly Dictionary<string, IChannelAdapter> _byChannel =
        adapters.ToDictionary(a => a.Channel, StringComparer.OrdinalIgnoreCase);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = $"{_kafka.ConsumerGroup}-outbound",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_kafka.OutboundTopic);
        logger.LogInformation("[outbound] consuming {Topic}", _kafka.OutboundTopic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "[outbound] consume error");
                    continue;
                }

                if (result?.Message?.Value is null)
                    continue;

                ChannelReply? reply;
                try
                {
                    reply = JsonSerializer.Deserialize<ChannelReply>(result.Message.Value, json);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "[outbound] could not parse reply, skipping");
                    continue;
                }

                if (reply is null)
                    continue;

                if (!_byChannel.TryGetValue(reply.Channel, out var adapter))
                {
                    logger.LogWarning("[outbound] no adapter registered for channel {Channel}", reply.Channel);
                    continue;
                }

                var send = await adapter.SendAsync(reply, ct);
                if (send.Ok)
                    logger.LogInformation("[outbound] sent via {Channel} -> {MessageId}", reply.Channel, send.ChannelMessageId);
                else
                    logger.LogError("[outbound] send failed via {Channel}: {Error}", reply.Channel, send.Error);
            }
        }
        finally
        {
            consumer.Close();
        }
    }
}

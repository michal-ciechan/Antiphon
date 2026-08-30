using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Messaging;

/// <summary>
/// CARD-0245 S2b: consume <c>channels.ops.inbound-unconsumed</c> into
/// <see cref="ChannelIngressIncidentService"/>. Dedicated group, never the inbound consumer group.
/// </summary>
public sealed class InboundUnconsumedEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<AntiphonMessagingOptions> options,
    ILogger<InboundUnconsumedEventConsumer> logger) : BackgroundService
{
    private readonly AntiphonMessagingOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var topic = string.IsNullOrWhiteSpace(_options.InboundUnconsumedTopic)
            ? "channels.ops.inbound-unconsumed"
            : _options.InboundUnconsumedTopic;
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = $"{_options.ConsumerGroup}-ops-unconsumed",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            MaxPartitionFetchBytes = _options.MaxMessageBytes,
            FetchMaxBytes = Math.Max(_options.MaxMessageBytes, 50 * 1024 * 1024),
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        logger.LogInformation("[ingress-ops] consuming {Topic} as {Group}", topic, config.GroupId);

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
                    logger.LogWarning(ex, "[ingress-ops] consume error");
                    continue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                if (result?.Message?.Value is null)
                    continue;

                InboundUnconsumedEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<InboundUnconsumedEvent>(result.Message.Value, Antiphon.Messaging.MessagingJson.Options);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "[ingress-ops] could not parse operational event; skipping");
                    continue;
                }

                if (evt is null)
                    continue;

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var persister = scope.ServiceProvider.GetRequiredService<ChannelIngressIncidentService>();
                    await persister.PersistAsync(evt, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "[ingress-ops] persist failed for {Topic}/{Partition}:{Offset}",
                        evt.Topic, evt.Partition, evt.Offset);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }
}

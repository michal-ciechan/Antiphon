using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Service;

/// <summary>Consumes the inbound topic and persists each message to the Postgres inbox (idempotent).</summary>
public sealed class InboxConsumerService(
    IInboundReceiptSink sink,
    IOptions<AntiphonGatewayOptions> options,
    JsonSerializerOptions json,
    ILogger<InboxConsumerService> logger) : BackgroundService
{
    private readonly AntiphonGatewayOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = $"{_options.ConsumerGroup}-inbox",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            // Match the bus cap (20 MB) — inbound messages may carry attachment payloads too.
            MaxPartitionFetchBytes = _options.MaxMessageBytes,
            FetchMaxBytes = Math.Max(_options.MaxMessageBytes, 50 * 1024 * 1024),
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.ResolveInboundTopic());
        logger.LogInformation("[inbox] consuming {Topic}", _options.ResolveInboundTopic());

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
                    logger.LogWarning(ex, "[inbox] consume error");
                    continue;
                }

                if (result?.Message?.Value is null)
                    continue;

                ChannelMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<ChannelMessage>(result.Message.Value, json);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "[inbox] could not parse envelope, skipping");
                    continue;
                }

                if (message is not null)
                {
                    try
                    {
                        await PersistAsync(message, result, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException
                                               && InboxUniqueConstraint.IsViolation(ex))
                    {
                        // Duplicate (Channel, ChannelMessageId) is a lost insert race. Any other
                        // persist failure must crash the host: EnableAutoCommit would otherwise
                        // advance the Kafka offset with no Inbox row and no monitor signal.
                        logger.LogWarning(ex, "[inbox] persist failed for {Channel} {MessageId}",
                            message.Channel, message.ChannelMessageId);
                    }
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task PersistAsync(ChannelMessage message, ConsumeResult<string, string> result, CancellationToken ct)
    {
        await sink.RecordAsync(
            message, result.Message.Value, result.Topic, result.Partition.Value, result.Offset.Value, ct);
        logger.LogInformation("[inbox] stored {Channel} {MessageId} {Topic}/{Partition}:{Offset}",
            message.Channel, message.ChannelMessageId, result.Topic, result.Partition.Value, result.Offset.Value);
    }
}

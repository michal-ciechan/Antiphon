using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>Consumes the outbound topic and dispatches each reply through the matching channel adapter.</summary>
public sealed class GatewayOutboundService(
    IEnumerable<IChannelAdapter> adapters,
    IOptions<AntiphonGatewayOptions> options,
    ILogger<GatewayOutboundService> logger,
    IOutboundAnswerReceiptSink? receipts) : BackgroundService
{
    public GatewayOutboundService(IEnumerable<IChannelAdapter> adapters, IOptions<AntiphonGatewayOptions> options,
        ILogger<GatewayOutboundService> logger) : this(adapters, options, logger, null) { }

    private readonly AntiphonGatewayOptions _options = options.Value;
    private readonly Dictionary<string, IChannelAdapter> _byChannel =
        adapters.ToDictionary(a => a.Channel, StringComparer.OrdinalIgnoreCase);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        if (!Enum.TryParse<AutoOffsetReset>(_options.AutoOffsetReset, ignoreCase: true, out var offsetReset))
        {
            throw new InvalidOperationException(
                $"AntiphonGateway:AutoOffsetReset '{_options.AutoOffsetReset}' is not a valid {nameof(AutoOffsetReset)} value.");
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = $"{_options.ConsumerGroup}-outbound",
            AutoOffsetReset = offsetReset,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            // Attachment-bearing replies can be up to the bus cap (20 MB); the per-partition
            // fetch default (1 MB) would stall on them.
            MaxPartitionFetchBytes = _options.MaxMessageBytes,
            FetchMaxBytes = Math.Max(_options.MaxMessageBytes, 50 * 1024 * 1024),
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.ResolveOutboundTopic());
        logger.LogInformation("[outbound] consuming {Topic}", _options.ResolveOutboundTopic());

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

                if (result is null) continue;

                ChannelReply? reply;
                try
                {
                    reply = result.Message?.Value is { } value
                        ? JsonSerializer.Deserialize<ChannelReply>(value, MessagingJson.Options) : null;
                }
                catch (JsonException)
                {
                    logger.LogWarning("[outbound] malformed reply at {Position}", result.TopicPartitionOffset);
                    reply = null;
                }

                if (reply is not null)
                    await DispatchAsync(reply, $"{result.Topic}/{result.Partition.Value}/{result.Offset.Value}", ct);
                // Includes explicitly discarded tombstones and malformed records. A journal
                // or commit failure exits this loop so a later record cannot skip this offset.
                consumer.Commit(result);
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    internal Task DispatchAsync(ChannelReply reply, CancellationToken ct) => DispatchAsync(reply, null, ct);

    private async Task DispatchAsync(ChannelReply reply, string? operationId, CancellationToken ct)
    {
        var journal = receipts as IOutboundOperationJournal;
        if (operationId != null && journal != null && !await journal.TryBeginSendAsync(operationId, reply, ct))
        {
            logger.LogInformation("[outbound] previously admitted operation {OperationId}; retaining its provider disposition", operationId);
            return;
        }

        if (!_byChannel.TryGetValue(reply.Channel, out var adapter))
        {
            logger.LogWarning("[outbound] no adapter registered for channel {Channel}, operation {OperationId}", reply.Channel, operationId);
            return;
        }

        var send = await adapter.SendAsync(reply, ct);
        if (send.Ok)
        {
            logger.LogInformation("[outbound] sent via {Channel} -> {MessageId}", reply.Channel, send.ChannelMessageId);
            if ((operationId != null && journal != null) || (receipts != null && reply.Kind == ChannelReplyKind.Answer
                && reply.ConversationId is { Length: > 0 } && reply.AnsweredMessageIds is { Count: > 0 }))
            {
                // The provider has accepted the send. Retry only the idempotent receipt write.
                var delay = TimeSpan.FromSeconds(1);
                while (true)
                {
                    try
                    {
                        if (operationId != null && journal != null)
                        {
                            if (!await journal.ConfirmSendAsync(operationId, send, ct))
                                logger.LogCritical("[outbound] conflicting native receipt for operation {OperationId}", operationId);
                        }
                        else
                            await receipts!.RecordAnswerAsync(reply, send, ct);
                        break;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        logger.LogError(ex, "Recording provider acceptance for {Channel} {MessageId} failed", reply.Channel, send.ChannelMessageId);
                        await Task.Delay(delay, ct);
                        delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                    }
                }
            }
        }
        else
            logger.LogError("[outbound] provider did not confirm {Channel} operation {OperationId}", reply.Channel, operationId);
    }
}

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
    ILogger<GatewayOutboundService> logger) : BackgroundService
{
    private readonly AntiphonGatewayOptions _options = options.Value;
    private readonly Dictionary<string, IChannelAdapter> _byChannel =
        adapters.ToDictionary(a => a.Channel, StringComparer.OrdinalIgnoreCase);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = $"{_options.ConsumerGroup}-outbound",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
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

                if (result?.Message?.Value is null)
                    continue;

                ChannelReply? reply;
                try
                {
                    reply = JsonSerializer.Deserialize<ChannelReply>(result.Message.Value, MessagingJson.Options);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "[outbound] could not parse reply, skipping");
                    continue;
                }

                if (reply is null)
                    continue;

                await DispatchAsync(reply, ct);
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    internal async Task DispatchAsync(ChannelReply reply, CancellationToken ct)
    {
        if (!_byChannel.TryGetValue(reply.Channel, out var adapter))
        {
            logger.LogWarning("[outbound] no adapter registered for channel {Channel}", reply.Channel);
            return;
        }

        var send = await adapter.SendAsync(reply, ct);
        if (send.Ok)
            logger.LogInformation("[outbound] sent via {Channel} -> {MessageId}", reply.Channel, send.ChannelMessageId);
        else
            logger.LogError("[outbound] send failed via {Channel}: {Error}", reply.Channel, send.Error);
    }
}

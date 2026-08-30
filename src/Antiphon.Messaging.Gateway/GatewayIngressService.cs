using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Runs each channel adapter's ingress loop and publishes every normalized <c>ChannelMessage</c>
/// to the inbound topic (keyed by conversation id for per-chat ordering).
/// </summary>
public sealed class GatewayIngressService : BackgroundService
{
    private readonly IEnumerable<IChannelAdapter> _adapters;
    private readonly IProducer<string, string> _producer;
    private readonly AntiphonGatewayOptions _options;
    private readonly ILogger<GatewayIngressService> _logger;
    private readonly IInboundReceiptSink[] _sinks;

    public GatewayIngressService(
        IEnumerable<IChannelAdapter> adapters,
        IProducer<string, string> producer,
        IOptions<AntiphonGatewayOptions> options,
        ILogger<GatewayIngressService> logger)
        : this(adapters, producer, options, logger, receiptSinks: null)
    {
    }

    public GatewayIngressService(
        IEnumerable<IChannelAdapter> adapters,
        IProducer<string, string> producer,
        IOptions<AntiphonGatewayOptions> options,
        ILogger<GatewayIngressService> logger,
        IEnumerable<IInboundReceiptSink>? receiptSinks)
    {
        _adapters = adapters;
        _producer = producer;
        _options = options.Value;
        _logger = logger;
        _sinks = receiptSinks?.ToArray() ?? [];
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_adapters.Select(adapter => PumpAsync(adapter, stoppingToken)));

    // The pump must NEVER die silently: an adapter's receive stream ending (or throwing) while the
    // host keeps running means the channel goes deaf with no visible symptom — that's exactly what
    // happened 2026-07-31 (AZ Care: an HttpClient timeout ended the stream; the service ran on for
    // 19h receiving nothing). The adapter is expected to loop forever itself; this restart loop is
    // the belt-and-braces layer, and it LOGS every restart so a flapping channel is visible.
    private async Task PumpAsync(IChannelAdapter adapter, CancellationToken ct)
    {
        var backoff = _options.IngressRestartBackoff;
        while (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("[ingress] starting channel {Channel}", adapter.Channel);
            try
            {
                await foreach (var message in adapter.ReceiveAsync(ct))
                {
                    try
                    {
                        var value = JsonSerializer.Serialize(message, MessagingJson.Options);
                        var delivery = await _producer.ProduceAsync(
                            _options.ResolveInboundTopic(),
                            new Message<string, string> { Key = message.Conversation.Id, Value = value },
                            ct);
                        _logger.LogInformation("[ingress] {Channel} {Conversation} -> {Topic}",
                            adapter.Channel, message.Conversation.Id, _options.ResolveInboundTopic());
                        foreach (var sink in _sinks)
                        {
                            try
                            {
                                await sink.RecordAsync(
                                    message, value, delivery.Topic, delivery.Partition.Value, delivery.Offset.Value, ct);
                            }
                            catch (Exception sinkEx) when (sinkEx is not OperationCanceledException)
                            {
                                _logger.LogWarning(sinkEx,
                                    "[ingress] receipt sink failed for {Channel} {MessageId}",
                                    adapter.Channel, message.ChannelMessageId);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "[ingress] failed to publish {Channel} message", adapter.Channel);
                    }
                }
                if (ct.IsCancellationRequested)
                    return;
                _logger.LogWarning(
                    "[ingress] {Channel} receive stream ended unexpectedly; restarting in {Backoff}s",
                    adapter.Channel, backoff.TotalSeconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ingress] {Channel} receive stream faulted; restarting in {Backoff}s",
                    adapter.Channel, backoff.TotalSeconds);
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}

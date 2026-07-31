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

    /// <summary>How long to wait before re-entering an adapter's receive stream after it ends/faults.</summary>
    internal static readonly TimeSpan RestartBackoff = TimeSpan.FromSeconds(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(adapters.Select(adapter => PumpAsync(adapter, stoppingToken)));

    // The pump must NEVER die silently: an adapter's receive stream ending (or throwing) while the
    // host keeps running means the channel goes deaf with no visible symptom — that's exactly what
    // happened 2026-07-31 (AZ Care: an HttpClient timeout ended the stream; the service ran on for
    // 19h receiving nothing). The adapter is expected to loop forever itself; this restart loop is
    // the belt-and-braces layer, and it LOGS every restart so a flapping channel is visible.
    private async Task PumpAsync(IChannelAdapter adapter, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            logger.LogInformation("[ingress] starting channel {Channel}", adapter.Channel);
            try
            {
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
                if (ct.IsCancellationRequested)
                    return;
                logger.LogWarning(
                    "[ingress] {Channel} receive stream ended unexpectedly; restarting in {Backoff}s",
                    adapter.Channel, RestartBackoff.TotalSeconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[ingress] {Channel} receive stream faulted; restarting in {Backoff}s",
                    adapter.Channel, RestartBackoff.TotalSeconds);
            }

            try { await Task.Delay(RestartBackoff, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}

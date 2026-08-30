using Antiphon.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>
/// CARD-0245 S2: once a minute, find inbox receipts older than the inbound-unconsumed budget
/// whose Antiphon consumer-group offset has not moved past them, acknowledge the original
/// conversation directly through <see cref="IChannelAdapter"/>, and publish an operational
/// event. Detection and acknowledgement only — never restarts AppHost.
/// </summary>
public sealed class InboundUnconsumedMonitorService : BackgroundService
{
    public const string AcknowledgementText =
        "[Antiphon] I received this message, but the service is unavailable. It is queued and will be processed when the service returns.";

    internal static readonly TimeSpan MaxAckBackoff = TimeSpan.FromMinutes(15);

    private readonly IInboxReceiptStore? _store;
    private readonly IConsumerGroupOffsetReader _offsets;
    private readonly IReadOnlyDictionary<string, IChannelAdapter> _adapters;
    private readonly IInboundUnconsumedEventPublisher _publisher;
    private readonly IAppHostHealthProbe _health;
    private readonly AntiphonGatewayOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<InboundUnconsumedMonitorService> _logger;

    public InboundUnconsumedMonitorService(
        IEnumerable<IInboxReceiptStore> stores,
        IConsumerGroupOffsetReader offsets,
        IEnumerable<IChannelAdapter> adapters,
        IInboundUnconsumedEventPublisher publisher,
        IAppHostHealthProbe health,
        IOptions<AntiphonGatewayOptions> options,
        TimeProvider time,
        ILogger<InboundUnconsumedMonitorService> logger)
    {
        _store = stores.FirstOrDefault();
        _offsets = offsets;
        _adapters = adapters.ToDictionary(a => a.Channel, StringComparer.OrdinalIgnoreCase);
        _publisher = publisher;
        _health = health;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.InboundUnconsumedMonitorEnabled || _store is null)
        {
            _logger.LogInformation("[inbound-unconsumed] monitor disabled or no inbox store");
            return;
        }

        var period = TimeSpan.FromSeconds(Math.Max(5, _options.InboundUnconsumedPollSeconds));
        using var timer = new PeriodicTimer(period);
        try
        {
            await TickAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One monitor pass. Public for tests.</summary>
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        if (_store is null || !_options.InboundUnconsumedMonitorEnabled)
            return 0;

        var now = _time.GetUtcNow();
        var cutoff = now - TimeSpan.FromMinutes(Math.Max(1, _options.InboundUnconsumedMinutes));
        IReadOnlyList<InboundReceipt> overdue;
        try
        {
            overdue = await _store.GetOverdueAsync(cutoff, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[inbound-unconsumed] inbox query failed");
            return 0;
        }

        var acted = 0;
        string? healthCache = null;
        async Task<string> HealthAsync() => healthCache ??= await _health.ProbeAsync(cancellationToken);
        foreach (var receipt in overdue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ProcessAsync(receipt, now, HealthAsync, cancellationToken))
                    acted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "[inbound-unconsumed] failed for {Channel} {MessageId}",
                    receipt.Channel, receipt.ChannelMessageId);
            }
        }

        return acted;
    }

    private async Task<bool> ProcessAsync(
        InboundReceipt receipt,
        DateTimeOffset now,
        Func<Task<string>> health,
        CancellationToken ct)
    {
        var committed = await _offsets.GetCommittedOffsetAsync(
            _options.AntiphonConsumerGroup, receipt.Topic, receipt.Partition, ct);
        if (!ConsumerLag.IsUnconsumed(committed, receipt.Offset))
            return false;

        var acknowledged = receipt.AcknowledgedAt.HasValue;
        string? ackError = null;
        if (!acknowledged && (receipt.NextAckAttemptAt is null || receipt.NextAckAttemptAt <= now))
        {
            (acknowledged, ackError) = await TryAcknowledgeAsync(receipt, now, ct);
        }

        if (receipt.OperationalEventPublishedAt is null)
        {
            var healthText = await health();
            await _publisher.PublishAsync(new InboundUnconsumedEvent
            {
                Provider = receipt.Channel,
                ConversationId = receipt.ConversationId,
                OriginalMessageId = receipt.ChannelMessageId,
                FirstSeenAt = receipt.FirstSeenAt,
                Topic = receipt.Topic,
                Partition = receipt.Partition,
                Offset = receipt.Offset,
                DetectedAt = now,
                Acknowledged = acknowledged,
                AcknowledgementError = ackError,
                AppHostHealth = healthText,
            }, ct);
            await _store!.MarkOperationalEventPublishedAsync(receipt.Id, now, ct);
        }

        return true;
    }

    private async Task<(bool Ok, string? Error)> TryAcknowledgeAsync(
        InboundReceipt receipt, DateTimeOffset now, CancellationToken ct)
    {
        if (!_adapters.TryGetValue(receipt.Channel, out var adapter))
        {
            var missing = $"no adapter for channel '{receipt.Channel}'";
            await ScheduleRetryAsync(receipt, now, missing, ct);
            return (false, missing);
        }

        try
        {
            var result = await adapter.SendAsync(new ChannelReply
            {
                Channel = receipt.Channel,
                Kind = ChannelReplyKind.Progress,
                ReplyHandle = receipt.ReplyHandle,
                ConversationId = receipt.ConversationId,
                ReplyToMessageId = receipt.ChannelMessageId,
                Text = AcknowledgementText,
            }, ct);
            if (result.Ok)
            {
                await _store!.MarkAcknowledgedAsync(receipt.Id, now, ct);
                _logger.LogWarning(
                    "[inbound-unconsumed] acknowledged {Channel} {Conversation} {MessageId}",
                    receipt.Channel, receipt.ConversationId, receipt.ChannelMessageId);
                return (true, null);
            }

            await ScheduleRetryAsync(receipt, now, result.Error ?? "send failed", ct);
            return (false, result.Error ?? "send failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await ScheduleRetryAsync(receipt, now, ex.Message, ct);
            return (false, ex.Message);
        }
    }

    private async Task ScheduleRetryAsync(InboundReceipt receipt, DateTimeOffset now, string error, CancellationToken ct)
    {
        var attempt = receipt.AckAttemptCount + 1;
        var delay = TimeSpan.FromMinutes(Math.Min(MaxAckBackoff.TotalMinutes, Math.Pow(2, Math.Min(attempt, 10) - 1)));
        await _store!.ScheduleAckRetryAsync(receipt.Id, now + delay, attempt, ct);
        _logger.LogWarning(
            "[inbound-unconsumed] ack failed for {Channel} {MessageId} (attempt {Attempt}, next {Next}): {Error}",
            receipt.Channel, receipt.ChannelMessageId, attempt, now + delay, error);
    }
}

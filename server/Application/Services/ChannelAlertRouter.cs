using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Routing half of the alert pipeline: fans a persisted alert into the per-sink throttle for every
/// catalog channel whose <c>AlertMinSeverity</c> admits it. Actual sending happens in
/// <see cref="AlertDigestFlusher"/> when a sink's hard window elapses.
/// </summary>
public sealed class ChannelAlertRouter : IAlertRouter
{
    private readonly AppDbContext _db;
    private readonly AlertThrottle _throttle;
    private readonly AlertsSettings _settings;
    private readonly ILogger<ChannelAlertRouter> _logger;

    public ChannelAlertRouter(
        AppDbContext db,
        AlertThrottle throttle,
        IOptions<AlertsSettings> settings,
        ILogger<ChannelAlertRouter> logger)
    {
        _db = db;
        _throttle = throttle;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task RouteAsync(Guid alertId, CancellationToken ct)
    {
        if (!_settings.RoutingEnabled)
            return;

        try
        {
            var alert = await _db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == alertId, ct);
            if (alert is null)
                return;

            var sinks = await _db.ChatChannels
                .AsNoTracking()
                .Where(c => c.AlertMinSeverity != null && alert.Severity >= c.AlertMinSeverity)
                .Select(c => c.Id)
                .ToListAsync(ct);

            foreach (var sinkId in sinks)
                _throttle.Enqueue(sinkId, alert);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Alert routing failed for {AlertId}", alertId);
        }
    }
}

/// <summary>
/// Sending half: drains due sinks from the throttle on a short tick and delivers ONE grouped
/// digest per sink through the existing messaging outbound path (producer -> channels.outbound ->
/// gateway -> provider adapter). Marks exemplar alerts routed with their suppressed counts.
/// </summary>
public sealed class AlertDigestFlusher
{
    private static readonly Dictionary<AlertSeverity, string> SeverityEmoji = new()
    {
        [AlertSeverity.Critical] = "\U0001F534", // red circle
        [AlertSeverity.Error] = "\U0001F7E0",    // orange circle
        [AlertSeverity.Warning] = "\U0001F7E1",  // yellow circle
        [AlertSeverity.Info] = "\U0001F535",     // blue circle
    };

    private readonly AppDbContext _db;
    private readonly AlertThrottle _throttle;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly AlertsSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertDigestFlusher> _logger;

    public AlertDigestFlusher(
        AppDbContext db,
        AlertThrottle throttle,
        IAntiphonMessagingProducer producer,
        IOptions<AlertsSettings> settings,
        TimeProvider timeProvider,
        ILogger<AlertDigestFlusher> logger)
    {
        _db = db;
        _throttle = throttle;
        _producer = producer;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<int> FlushDueAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var due = _throttle.CollectDue(
            now, TimeSpan.FromMinutes(_settings.MinMinutesBetweenSends), _settings.CriticalBypassWindow);
        if (due.Count == 0)
            return 0;

        var sent = 0;
        foreach (var (sinkId, groups) in due)
        {
            ct.ThrowIfCancellationRequested();
            var sink = await _db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sinkId, ct);
            if (sink is null || sink.AlertMinSeverity is null)
                continue; // sink deleted/disabled while pending — drop silently

            var text = Format(groups);
            try
            {
                await _producer.SendAsync(
                    new ChannelReply
                    {
                        Channel = sink.Provider,
                        ConversationId = sink.ExternalId,
                        Text = text,
                    },
                    ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Broker/gateway down: alerts stay recorded in the DB; delivery is best-effort.
                _logger.LogWarning(ex, "Alert digest delivery to sink {SinkId} failed", sinkId);
                continue;
            }

            foreach (var group in groups)
            {
                await _db.Alerts
                    .Where(a => a.Id == group.ExemplarAlertId)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(a => a.RoutedAt, now)
                        .SetProperty(a => a.SuppressedCount, group.Count - 1), ct);
            }
        }

        return sent;
    }

    private static string Format(IReadOnlyList<AlertThrottle.PendingGroup> groups)
    {
        var lines = new List<string> { "Antiphon alerts:" };
        foreach (var group in groups)
        {
            var emoji = SeverityEmoji.GetValueOrDefault(group.Severity, "⚪");
            var count = group.Count > 1 ? $" ×{group.Count}" : "";
            var detail = string.IsNullOrWhiteSpace(group.Detail)
                ? ""
                : $" — {Truncate(group.Detail!, 200)}";
            lines.Add($"{emoji} [{group.Source}] {group.Title}{count}{detail}");
        }

        return string.Join("\n", lines);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}

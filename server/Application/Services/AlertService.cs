using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AlertService : IAlertService
{
    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IAlertRouter _router;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        AppDbContext db,
        IEventBus eventBus,
        IAlertRouter router,
        TimeProvider timeProvider,
        ILogger<AlertService> logger)
    {
        _db = db;
        _eventBus = eventBus;
        _router = router;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RaiseAsync(AlertRaise raise, CancellationToken ct)
    {
        try
        {
            var alert = new Alert
            {
                Id = Guid.NewGuid(),
                Severity = raise.Severity,
                Source = raise.Source,
                AgentId = raise.AgentId,
                SessionId = raise.SessionId,
                Title = raise.Title,
                Detail = raise.Detail,
                DedupKey = raise.DedupKey ?? $"{raise.Source}:{raise.Title}",
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            };
            _db.Alerts.Add(alert);
            await _db.SaveChangesAsync(ct);

            await _eventBus.PublishToAllAsync(
                "AlertRaised",
                new
                {
                    id = alert.Id,
                    severity = alert.Severity.ToString(),
                    source = alert.Source,
                    title = alert.Title,
                    detail = alert.Detail,
                    agentId = alert.AgentId,
                    createdAt = alert.CreatedAt,
                },
                ct);

            await _router.RouteAsync(alert.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The alert pipeline must never take down its caller.
            _logger.LogWarning(ex, "Alert raise failed ({Source}: {Title})", raise.Source, raise.Title);
        }
    }
}

/// <summary>Slice-4 placeholder: alerts persist + SignalR only. Slice 5 replaces with channel routing.</summary>
public sealed class NullAlertRouter : IAlertRouter
{
    public Task RouteAsync(Guid alertId, CancellationToken ct) => Task.CompletedTask;
}

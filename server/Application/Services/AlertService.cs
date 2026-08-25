using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AlertService : IAlertService
{
    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IAlertRouter _router;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        AppDbContext db,
        IEventBus eventBus,
        IAlertRouter router,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AlertService> logger)
    {
        _db = db;
        _eventBus = eventBus;
        _router = router;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RaiseAsync(AlertRaise raise, CancellationToken ct)
    {
        // Clipped BEFORE the write, not caught after it (CARD-0205). A detail too long for its
        // column used to fail the insert outright, and because the catch below only logs, the alert
        // simply never existed — for four days the pty-host census detector that was supposed to
        // catch CARD-0204's leak fired correctly every sweep and left no row anywhere. A clipped
        // report still names the problem; a rejected one names nothing.
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            Severity = raise.Severity,
            Source = ColumnText.Clip(raise.Source, Alert.SourceMaxLength),
            AgentId = raise.AgentId,
            SessionId = raise.SessionId,
            Title = ColumnText.Clip(raise.Title, Alert.TitleMaxLength),
            Detail = ColumnText.ClipOrNull(raise.Detail, Alert.DetailMaxLength),
            DedupKey = ColumnText.Clip(
                raise.DedupKey ?? $"{raise.Source}:{raise.Title}", Alert.DedupKeyMaxLength),
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };

        try
        {
            _db.Alerts.Add(alert);
            await _db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Forget(alert);
            throw;
        }
        catch (Exception ex)
        {
            if (!await PersistOutOfBandAsync(alert, ex, ct))
                return;
        }

        try
        {
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

    /// <summary>
    /// Last resort when the caller's own <see cref="AppDbContext"/> refuses the save.
    ///
    /// <para>This exists because the context is SHARED: it is the caller's scoped instance, holding
    /// whatever else that caller has staged. In CARD-0205 the reconciler's orphan alert overflowed
    /// its column and stayed <c>Added</c> in the change tracker — EF does not evict a failed insert
    /// — so the very next raise in the same sweep, the pty-host census alert whose own detail was
    /// 617 perfectly legal characters, was re-submitted alongside the poison row and died with it.
    /// The detector's alert was never the oversized one; it was standing next to it.</para>
    ///
    /// <para>So: drop our row from the shared tracker (which also unblocks every later save in that
    /// scope), then try once more on a context of our own, where nobody else's pending work can
    /// take us down. Returns whether the alert is now persisted.</para>
    /// </summary>
    private async Task<bool> PersistOutOfBandAsync(Alert alert, Exception cause, CancellationToken ct)
    {
        Forget(alert);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Alerts.Add(alert);
            await db.SaveChangesAsync(ct);
            db.Entry(alert).State = EntityState.Detached;

            _logger.LogWarning(
                cause,
                "Alert raise could not use the caller's DbContext ({Source}: {Title}); it was saved "
                + "on a context of its own instead. Something else staged on that context is failing "
                + "to save.",
                alert.Source, alert.Title);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Alert raise failed ({Source}: {Title})", alert.Source, alert.Title);
            return false;
        }
    }

    /// <summary>
    /// Untracks our alert. A failed <c>SaveChangesAsync</c> leaves the entity <c>Added</c>, so
    /// leaving it there makes every subsequent save on this shared context retry — and re-fail on —
    /// a row its caller knows nothing about.
    /// </summary>
    private void Forget(Alert alert)
    {
        try
        {
            _db.Entry(alert).State = EntityState.Detached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detach the failed alert from the caller's DbContext");
        }
    }
}

/// <summary>Slice-4 placeholder: alerts persist + SignalR only. Slice 5 replaces with channel routing.</summary>
public sealed class NullAlertRouter : IAlertRouter
{
    public Task RouteAsync(Guid alertId, CancellationToken ct) => Task.CompletedTask;
}

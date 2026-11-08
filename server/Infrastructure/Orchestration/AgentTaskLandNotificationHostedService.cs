using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Boot scan and periodic backstop. Row failures cannot prevent later pages running.</summary>
public sealed class AgentTaskLandNotificationHostedService(IServiceScopeFactory scopes,
    ILogger<AgentTaskLandNotificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try
                {
                    Guid? intentCursor = null;
                    var now = DateTime.UtcNow;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        now = DateTime.UtcNow;
                        var query = db.AgentTaskDispatchWarningIntents.AsNoTracking()
                            .Where(i => i.MaterializedAt == null && i.NextAttemptAt <= now);
                        if (intentCursor is Guid after) query = query.Where(i => i.Id.CompareTo(after) > 0);
                        var intentIds = await query.OrderBy(i => i.Id).Select(i => i.Id).Take(128)
                            .ToListAsync(stoppingToken);
                        foreach (var id in intentIds)
                        {
                            try
                            {
                                await using var rowScope = scopes.CreateAsyncScope();
                                await rowScope.ServiceProvider.GetRequiredService<DispatchBaseWarningIntentService>()
                                    .MaterializeAsync(id, stoppingToken);
                            }
                            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                            { logger.LogWarning(ex, "Dispatch-base warning intent {IntentId} remains unresolved", id); }
                        }
                        if (intentIds.Count < 128) break;
                        intentCursor = intentIds[^1];
                    }
                    await using var intentObservation = scopes.CreateAsyncScope();
                    if (intentObservation.ServiceProvider.GetService<LandDeliveryBoundary>() is { } intentBoundary)
                        await intentBoundary.ReachedAsync("dispatch-warning-intent-scan", Guid.Empty, Guid.Empty, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { logger.LogWarning(ex, "Dispatch-base warning intent scan failed"); }

                // CARD-1705: callers whose queue lock stayed held this pass; their notices wait for the next.
                // One set spans the captured and land-notification phases, so both keep each caller's order.
                var busySessions = new HashSet<Guid>();
                var capturedEnabled = false;
                await using (var probe = scopes.CreateAsyncScope())
                    capturedEnabled = probe.ServiceProvider.GetService<LegacyCheckNotePublicationService>() is not null;
                if (capturedEnabled)
                {
                    Guid? capturedCursor = null;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var now = DateTime.UtcNow;
                        var capturedQuery = db.LegacyCheckNotePublications.AsNoTracking()
                            .Where(p => p.State == LegacyCheckNoteState.Captured && p.NextAttemptAt <= now);
                        if (capturedCursor is Guid capturedAfter)
                            capturedQuery = capturedQuery.Where(p => p.Id.CompareTo(capturedAfter) > 0);
                        var capturedIds = await capturedQuery.OrderBy(p => p.Id).Select(p => p.Id).Take(128)
                            .ToListAsync(stoppingToken);
                        foreach (var id in capturedIds)
                        {
                            try
                            {
                                await using var rowScope = scopes.CreateAsyncScope();
                                await rowScope.ServiceProvider.GetRequiredService<LegacyCheckNotePublicationService>()
                                    .RecoverCapturedAsync(id, stoppingToken, busySessions);
                            }
                            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                            { logger.LogWarning(ex, "Captured legacy Check publication {PublicationId} remains unresolved", id); }
                        }

                        if (capturedIds.Count < 128)
                            break;
                        capturedCursor = capturedIds[^1];
                    }

                    await using var capturedObservation = scopes.CreateAsyncScope();
                    if (capturedObservation.ServiceProvider.GetService<CheckCompactionBoundary>() is { } capturedBoundary)
                        await capturedBoundary.ReachedAsync("legacy-captured-scan", Guid.Empty, stoppingToken);
                }

                await ScanLandNotificationsOnceAsync(stoppingToken, busySessions);
                await using var observation = scopes.CreateAsyncScope();
                if (observation.ServiceProvider.GetService<LandDeliveryBoundary>() is { } boundary)
                    await boundary.ReachedAsync("notification-scan", Guid.Empty, Guid.Empty, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Land notification scan failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    /// <summary>
    /// One land-notification pass, in id order. The hosted loop and the CARD-1769 measurement
    /// share it. <paramref name="busySessions"/> is the caller's queue-lock set for this pass.
    /// </summary>
    internal async Task ScanLandNotificationsOnceAsync(CancellationToken stoppingToken, ISet<Guid>? busySessions = null)
    {
        Guid? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // CARD-1759. Skip a null-queue note whose next attempt is still ahead: reconcile
            // returns before writing. A provider-input wait aimed anywhere but a session writes
            // first, so it stays in the page. The clock is this page's read. A note that becomes
            // due after it waits out the rest of this scan and the 5-second period below.
            // The compiled page caches the plan, not the rows.
            var now = (scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var ids = new List<Guid>(128);
            var page = cursor is Guid after
                ? LandNotificationReconcileQueries.DueNoteIdsAfter(db, now, after)
                : LandNotificationReconcileQueries.DueNoteIds(db, now);
            await foreach (var id in page.WithCancellation(stoppingToken))
                ids.Add(id);
            foreach (var id in ids)
            {
                try
                {
                    await using var rowScope = scopes.CreateAsyncScope();
                    await rowScope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                        .ReconcileAsync(id, stoppingToken, busySessions);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { logger.LogWarning(ex, "Land notification {NotificationId} remains unresolved", id); }
            }
            if (ids.Count < 128) break;
            cursor = ids[^1];
        }
    }

    /// <summary>CARD-1769. The rescan park's cap. A note past its backoff is reconciled on the next pass.</summary>
    internal void ExpireRescanPark(Guid noteId)
    {
        _ = noteId;
    }
}

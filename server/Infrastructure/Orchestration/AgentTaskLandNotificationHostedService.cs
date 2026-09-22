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
                                    .RecoverCapturedAsync(id, stoppingToken);
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

                Guid? cursor = null;
                while (!stoppingToken.IsCancellationRequested)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var query = db.AgentTaskLandNotifications.AsNoTracking().Where(n =>
                        n.State != LandNotificationState.Confirmed && n.State != LandNotificationState.NotRequired && n.State != LandNotificationState.LegacyUnverified);
                    if (cursor is Guid after) query = query.Where(n => n.Id.CompareTo(after) > 0);
                    var ids = await query.OrderBy(n => n.Id).Select(n => n.Id).Take(128).ToListAsync(stoppingToken);
                    foreach (var id in ids)
                    {
                        try
                        {
                            await using var rowScope = scopes.CreateAsyncScope();
                            await rowScope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                                .ReconcileAsync(id, stoppingToken);
                        }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        { logger.LogWarning(ex, "Land notification {NotificationId} remains unresolved", id); }
                    }
                    if (ids.Count < 128) break;
                    cursor = ids[^1];
                }
                await using var observation = scopes.CreateAsyncScope();
                if (observation.ServiceProvider.GetService<LandDeliveryBoundary>() is { } boundary)
                    await boundary.ReachedAsync("notification-scan", Guid.Empty, Guid.Empty, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Land notification scan failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

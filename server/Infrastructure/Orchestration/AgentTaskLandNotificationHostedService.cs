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

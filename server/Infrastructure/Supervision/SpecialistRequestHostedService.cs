using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Progresses already-authorized requests and qualification; never grants or launches a standing process.</summary>
public sealed class SpecialistRequestHostedService(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<SpecialistRequestHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SpecialistRequestService>().ReconcileAsync(stoppingToken);
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var unpublished = await db.SpecialistRequests.AsNoTracking().Where(r => r.Purpose == SpecialistRequestPurpose.Check
                        && r.CompletedAt != null && r.CallerPublishedAt == null).OrderBy(r => r.StartedAt).Take(16).ToListAsync(stoppingToken);
                    foreach (var request in unpublished)
                    {
                        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.CheckedTaskId, stoppingToken);
                        if (task is null || AgentTaskService.IsSettled(task.Status) || Math.Max(1, task.CheckCount) != request.CheckNumber
                            || request.Status == SpecialistRequestStatus.Canceled
                            || !await db.Agents.AnyAsync(a => a.Id == request.AgentId, stoppingToken))
                        {
                            await db.SpecialistRequests.Where(r => r.Id == request.Id && r.CallerPublishedAt == null)
                                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CallerPublishedAt, time.GetUtcNow().UtcDateTime), stoppingToken);
                            continue;
                        }
                        // RunCheck reloads the immutable snapshot and reuses the terminal request.
                        // It cannot create another model attempt or another publication identity.
                        await scope.ServiceProvider.GetRequiredService<AgentTaskCheckService>().RunCheckAsync(task.Id, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { logger.LogWarning(ex, "Specialist request reconciliation failed; durable deadlines remain authoritative"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Owns deferred completion flushes, independent of the optional distiller reader.</summary>
public sealed class CompletionNoteWorkHostedService(
    IServiceScopeFactory scopes, CompletionNoteFlushQueue flushes, SpecialistFailureQueue failures,
    TimeProvider clock, ILogger<CompletionNoteWorkHostedService> logger) : BackgroundService
{
    private int _scanOffset;
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        ScanAsync(stoppingToken), FlushAsync(stoppingToken), FlushAsync(stoppingToken),
        FlushAsync(stoppingToken), FlushAsync(stoppingToken), IncidentsAsync(stoppingToken));

    private async Task ScanAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var sessions = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0
                        && m.SourceTaskId != null && m.ContentDigest != null
                        && (m.HoldUntil == null || m.HoldUntil <= now))
                    .Select(m => m.AgentSessionId).Distinct().OrderBy(id => id)
                    .Skip(_scanOffset).Take(128).ToListAsync(ct);
                _scanOffset = sessions.Count == 128 ? _scanOffset + 128 : 0;
                foreach (var session in sessions)
                {
                    if (scope.ServiceProvider.GetService<LandDeliveryBoundary>() is { } boundary)
                        await boundary.ReachedAsync("completion-scan", Guid.Empty, session, ct);
                    flushes.TryEnqueue(session);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "Completion note recovery scan failed"); }
            await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        await foreach (var session in flushes.ReadAllAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>().FlushIfIdleAsync(session, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "Deferred completion flush failed for {SessionId}", session); }
            finally { flushes.Complete(session); }
        }
    }

    private async Task IncidentsAsync(CancellationToken ct)
    {
        await foreach (var failure in failures.ReadAllAsync(ct))
        {
            using var budget = new ExecutionBudget(clock.GetUtcNow().AddSeconds(2), clock, ct);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var runner = new SpecialistTaskRunner(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    clock, logger, scope.ServiceProvider.GetService<IAlertService>());
                await runner.RaiseUnavailableAsync(failure.Spec, null, failure.Reason, budget.Token);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "Deferred specialist incident did not finish within its allowance"); }
        }
    }
}

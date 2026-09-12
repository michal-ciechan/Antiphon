using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

                await RecoverMissingSourcedCompletionNotesAsync(scope.ServiceProvider, db, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "Completion note recovery scan failed"); }
            await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
        }
    }

    /// <summary>
    /// G-150: a sourced Mutation Result that survived an enqueue fault is still owed a caller note.
    /// Pending-row wakeup cannot see a missing insert; rebuild from the durable task.
    /// </summary>
    private async Task RecoverMissingSourcedCompletionNotesAsync(
        IServiceProvider services, AppDbContext db, CancellationToken ct)
    {
        var owed = await db.AgentTasks.AsNoTracking()
            .Where(t => t.SourceLandingOperationId != null
                && t.ParentSessionId != null
                && t.ReplyTo == AgentTaskReplyTo.Session
                && t.Result != null
                && (t.Status == AgentTaskStatus.Succeeded
                    || t.Status == AgentTaskStatus.Failed
                    || t.Status == AgentTaskStatus.Canceled))
            .Select(t => new { t.Id, Parent = t.ParentSessionId!.Value, t.RootTaskId })
            .ToListAsync(ct);
        if (owed.Count == 0)
            return;

        var queue = services.GetRequiredService<SessionMessageQueueService>();
        var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        foreach (var row in owed)
        {
            if (await AgentTaskCheckService.HasCompletionNoteAsync(db, row.Parent, row.RootTaskId, ct))
                continue;
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.Id, ct);
            if (services.GetService<LandDeliveryBoundary>() is { } boundary)
                await boundary.ReachedAsync("completion-scan", task.Id, row.Parent, ct);
            var report = task.Result ?? "";
            var note = DelegationReportFormatter.BuildCompletionNote(
                task, settings, report, land: await LandCompletionFacts.LoadAsync(db, task, ct));
            await queue.EnqueueAsync(
                row.Parent, note.Body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                task.Id, DelegationNoteDigest.Compute(report), note.Header, deliverIfIdle: false);
            flushes.TryEnqueue(row.Parent);
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

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
    private const int PageSize = 128;
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        RecoverAsync(stoppingToken), FlushAsync(stoppingToken), FlushAsync(stoppingToken),
        FlushAsync(stoppingToken), FlushAsync(stoppingToken), IncidentsAsync(stoppingToken));

    private async Task RecoverAsync(CancellationToken ct)
    {
        var nextSweep = clock.GetUtcNow().UtcDateTime;
        var failuresInARow = 0;
        while (!ct.IsCancellationRequested)
        {
            var work = flushes.Recovery.Take(clock.GetUtcNow().UtcDateTime);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (work.Sweep || clock.GetUtcNow().UtcDateTime >= nextSweep)
                {
                    await SweepAsync(scope.ServiceProvider, db, ct);
                    nextSweep = clock.GetUtcNow().UtcDateTime.AddMinutes(15);
                }
                else
                {
                    foreach (var task in work.Tasks)
                        await RecoverMissingAsync(scope.ServiceProvider, db, task, ct);
                }
                foreach (var session in work.Sessions) flushes.TryEnqueue(session);
                failuresInARow = 0;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Completion note recovery failed; requesting a backstop sweep");
                flushes.Recovery.RequestSweep();
                // First error gets an immediate recovery pass. A broken database must not
                // become a tight retry loop; all failed work remains durable.
                if (++failuresInARow > 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, failuresInARow)), clock, ct);
            }
            await flushes.Recovery.WaitAsync(nextSweep, clock, ct);
        }
    }

    private async Task SweepAsync(IServiceProvider services, AppDbContext db, CancellationToken ct)
    {
        await RecoverMissingAsync(services, db, null, ct);
        Guid? cursor = null;
        while (true)
        {
            var query = db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0
                    && m.SourceTaskId != null && m.ContentDigest != null);
            if (cursor is Guid after) query = query.Where(m => m.Id.CompareTo(after) > 0);
            var rows = await query.OrderBy(m => m.Id).Take(PageSize)
                .Select(m => new { m.Id, m.AgentSessionId, m.HoldUntil }).ToListAsync(ct);
            foreach (var row in rows)
            {
                if (services.GetService<LandDeliveryBoundary>() is { } boundary)
                    await boundary.ReachedAsync("completion-scan", Guid.Empty, row.AgentSessionId, ct);
                flushes.Recovery.ScheduleFlush(row.AgentSessionId, row.HoldUntil ?? DateTime.MinValue);
            }
            if (rows.Count < PageSize) break;
            cursor = rows[^1].Id;
        }
    }

    /// <summary>
    /// Only unstamped sourced results can enter this walk. Keyset pages do not skip work when
    /// stamping shrinks the partial index, and already-stamped history is never materialized.
    /// The existing root receipt checks and atomic queue+stamp write remain authoritative.
    /// </summary>
    private async Task RecoverMissingAsync(IServiceProvider services, AppDbContext db, Guid? taskId, CancellationToken ct)
    {
        Guid? cursor = null;
        Exception? failed = null;
        while (true)
        {
            var query = db.AgentTasks.AsNoTracking()
                .Where(t => t.CompletionNoteQueuedAt == null
                    && t.SourceLandingOperationId != null && t.ParentSessionId != null
                    && t.ReplyTo == AgentTaskReplyTo.Session && t.Result != null
                    && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Failed
                        || t.Status == AgentTaskStatus.Canceled));
            if (taskId is Guid id) query = query.Where(t => t.Id == id);
            if (cursor is Guid after) query = query.Where(t => t.Id.CompareTo(after) > 0);
            var owed = await query.OrderBy(t => t.Id).Take(PageSize).ToListAsync(ct);
            if (owed.Count == 0) break;
            var ids = owed.Select(t => t.Id).ToArray();
            await CompletionNoteStamp.RepairFromAsync(db,
                db.SessionQueuedMessages.Where(m => m.SourceTaskId != null && ids.Contains(m.SourceTaskId.Value)), ct);
            var queue = services.GetRequiredService<SessionMessageQueueService>();
            var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
            foreach (var task in owed)
            {
                try
                {
                    var parent = task.ParentSessionId!.Value;
                    if (await AgentTaskCheckService.HasCompletionNoteAsync(db, parent, task.RootTaskId, ct)) continue;
                    if (services.GetService<LandDeliveryBoundary>() is { } boundary)
                        await boundary.ReachedAsync("completion-scan", task.Id, parent, ct);
                    var report = task.Result!;
                    var note = DelegationReportFormatter.BuildCompletionNote(
                        task, settings, report, land: await LandCompletionFacts.LoadAsync(db, task, ct));
                    await queue.EnqueueAsync(parent, note.Body, MessageSendMode.WhenIdle, ct,
                        QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                        task.Id, DelegationNoteDigest.Compute(report), note.Header, deliverIfIdle: false);
                    flushes.TryEnqueue(parent);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failed ??= ex;
                    logger.LogWarning(ex, "Completion recovery failed for {TaskId}; continuing the page", task.Id);
                }
            }
            if (taskId is not null || owed.Count < PageSize) break;
            cursor = owed[^1].Id;
        }
        if (failed is not null) throw new InvalidOperationException("Completion recovery has failed tasks to retry", failed);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        await foreach (var session in flushes.ReadAllAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>().FlushIfIdleAsync(session, ct);
                // Several held notes may share a caller. Once the earliest deadline fires,
                // retain the next one even if the caller has not started another turn.
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var nextHold = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == session && m.Status == QueuedMessageStatus.Pending
                        && m.DeliveryAttempts == 0 && m.SourceTaskId != null && m.ContentDigest != null
                        && m.HoldUntil > now)
                    .MinAsync(m => m.HoldUntil, ct);
                if (nextHold is DateTime due) flushes.Recovery.ScheduleFlush(session, due);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Deferred completion flush failed for {SessionId}", session);
                flushes.Recovery.RequestSweep();
            }
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

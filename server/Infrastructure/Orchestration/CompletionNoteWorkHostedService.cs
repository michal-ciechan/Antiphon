using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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

    /// <summary>
    /// CARD-1709: how long one recovery pass waits for a caller's queue lock before leaving that
    /// caller's completion notes for the next pass.
    /// </summary>
    internal static readonly TimeSpan ScanQueueLockWait = TimeSpan.FromSeconds(1);

    /// <summary>CARD-1709: a pass that left a busy session's work behind runs again this soon, not at the backstop.</summary>
    internal static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(5);

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
            foreach (var session in work.Sessions) flushes.TryEnqueue(session);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // CARD-1709: sessions whose queue lock stayed held this pass; their work waits for the next.
                var busySessions = new HashSet<Guid>();
                if (work.Sweep || clock.GetUtcNow().UtcDateTime >= nextSweep)
                {
                    await SweepAsync(scope.ServiceProvider, db, ct, busySessions);
                    nextSweep = clock.GetUtcNow().UtcDateTime.AddMinutes(15);
                }
                else
                {
                    foreach (var task in work.Tasks)
                        await RecoverMissingAsync(scope.ServiceProvider, db, task, ct, busySessions);
                }
                // Skipped work is unchanged and not a failure. The retry pass is a sweep, so it
                // finds it whichever task or sweep pass skipped it.
                if (busySessions.Count > 0)
                    nextSweep = Min(nextSweep, clock.GetUtcNow().UtcDateTime.Add(BusyRetryDelay));
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

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private async Task SweepAsync(IServiceProvider services, AppDbContext db, CancellationToken ct, ISet<Guid> busySessions)
    {
        Exception? recoveryFailure = null;
        try { await RecoverMissingAsync(services, db, null, ct, busySessions); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { recoveryFailure = ex; }
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
        if (recoveryFailure is not null)
            throw new InvalidOperationException("Completion recovery needs another sweep", recoveryFailure);
    }

    /// <summary>
    /// Only unstamped sourced results can enter this walk. Keyset pages do not skip work when
    /// stamping shrinks the partial index, and already-stamped history is never materialized.
    /// The existing root receipt checks and atomic queue+stamp write remain authoritative.
    /// </summary>
    /// <param name="busySessions">
    /// CARD-1709: the pass's busy set, shared by the supervision and completion-note phases. An enqueue
    /// waits at most <see cref="ScanQueueLockWait"/> for a session's queue lock; a session whose lock is
    /// still held joins the set, and its remaining items in this pass are skipped unchanged, in order.
    /// </param>
    private async Task RecoverMissingAsync(IServiceProvider services, AppDbContext db, Guid? taskId, CancellationToken ct,
        ISet<Guid>? busySessions = null)
    {
        busySessions ??= new HashSet<Guid>();
        Exception? failed = null;
        try
        {
            // A poisoned supervision item must not poison the completion-note context or skip
            // its independent recovery pass. Preserve the failure for the next sweep afterward.
            await using var supervisionScope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            await new TaskSupervisionNoticeService(supervisionScope.ServiceProvider.GetRequiredService<AppDbContext>(),
                supervisionScope.ServiceProvider.GetRequiredService<SessionMessageQueueService>(), clock).RecoverAsync(taskId, ct, busySessions);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failed = ex;
            logger.LogWarning(ex, "Supervision recovery failed; continuing completion-note recovery");
        }
        Guid? cursor = null;
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
                    if (busySessions.Contains(parent)) continue;
                    if (services.GetService<LandDeliveryBoundary>() is { } boundary)
                        await boundary.ReachedAsync("completion-scan", task.Id, parent, ct);
                    var report = task.Result!;
                    var note = DelegationReportFormatter.BuildCompletionNote(
                        task, settings, report, land: await LandCompletionFacts.LoadAsync(db, task, ct));
                    await queue.EnqueueAsync(parent, note.Body, MessageSendMode.WhenIdle, ct,
                        QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                        task.Id, DelegationNoteDigest.Compute(report), note.Header, deliverIfIdle: false,
                        lockWait: ScanQueueLockWait);
                    flushes.TryEnqueue(parent);
                }
                catch (SessionQueueBusyException busy)
                {
                    // Nothing was written under the lock. Not a failed attempt: the task stays owed, unstamped.
                    busySessions.Add(busy.SessionId);
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

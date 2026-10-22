using System.Collections.Concurrent;
using System.Threading.Channels;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Bounded execution hints backed by transactional transcript action debt.</summary>
public sealed class SessionRuntimeActionCoordinator(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<SessionRuntimeActionCoordinator> logger)
{
    internal const int WorkerCount = 4;
    internal const int HintCapacity = 128;
    internal const int PageSize = 128;
    internal const int QueuePhase = 8;
    internal const int ChannelReplyPhase = 4;
    // A channel reply that failed, or that waits behind one, is its own debt row so
    // a channel outage cannot hold settlement, refinement confirmation or catch-up.
    internal const string ChannelReplyDebt = "channel";
    private const string DeferredReason = "Deferred";
    private readonly Channel<Guid> _hints = Channel.CreateBounded<Guid>(new BoundedChannelOptions(HintCapacity)
        { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    // Contains active execution owners only, at most WorkerCount. Overflow lives in PostgreSQL.
    private readonly ConcurrentDictionary<Guid, byte> _owners = new();
    private readonly Channel<bool> _scan = Channel.CreateBounded<bool>(1);
    private int _running;

    internal bool Hint(Guid sessionId)
    {
        if (_hints.Writer.TryWrite(sessionId)) return true;
        _scan.Writer.TryWrite(true);
        try { logger.LogWarning("Action hints saturated for session {SessionId}; capacity {HintCapacity}, owners {OwnerCount}", sessionId, HintCapacity, _owners.Count); }
        catch { }
        return false;
    }

    internal static SessionRuntimeAction Admit(AppDbContext db, TranscriptEntry row,
        SessionRunnerTranscriptEvent source, DateTime generation, bool boundary, DateTime now,
        Guid? taskId = null, int? taskAttempt = null)
    {
        var action = new SessionRuntimeAction { Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId,
            TranscriptEntryId = row.Id, AcceptedGeneration = generation, Sequence = row.Sequence,
            AcceptedTaskId = taskId, AcceptedTaskAttempt = taskAttempt,
            ProducerSequence = source.Sequence, TurnBoundary = boundary, CreatedAt = now, DueAt = now,
            ManualCompactBoundary = TranscriptKinds.IsManualCompactBoundary(source.Kind, source.Text),
            UiProjectionDelta = TranscriptUiProjection.Capture(source, row) };
        db.SessionRuntimeActions.Add(action);
        return action;
    }

    internal async Task RequestAsync(Guid sessionId, string kind, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<SessionStateStore>();
        using var lease = await state.BeginWriteAsync(sessionId, ct);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generation = await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => (DateTime?)s.StartedAt).SingleOrDefaultAsync(ct);
        if (generation is null) return;
        var sequence = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
        var action = await db.SessionRuntimeActions.SingleOrDefaultAsync(a => a.AgentSessionId == sessionId
            && a.Sequence == sequence && a.TriggerKind == kind, ct);
        if (action is null)
        {
            var task = await db.AgentTasks.AsNoTracking().Where(t => t.AgentSessionId == sessionId
                && (t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Dispatched))
                .Select(t => new { t.Id, t.Attempt }).FirstOrDefaultAsync(ct);
            db.SessionRuntimeActions.Add(new SessionRuntimeAction { Id = Guid.NewGuid(), AgentSessionId = sessionId,
                AcceptedGeneration = generation, Sequence = sequence, TriggerKind = kind,
                AcceptedTaskId = task?.Id, AcceptedTaskAttempt = task?.Attempt,
                Phase = kind == "status" ? QueuePhase : 1, CreatedAt = clock.GetUtcNow().UtcDateTime,
                DueAt = clock.GetUtcNow().UtcDateTime });
        }
        else action.DueAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        Hint(sessionId);
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("Action workers already running.");
        try
        {
            await Task.WhenAll(Enumerable.Range(0, WorkerCount).Select(_ => WorkAsync(ct)).Append(DiscoverAsync(ct)));
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private async Task DiscoverAsync(CancellationToken ct)
    {
        Guid? cursor = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var query = db.SessionRuntimeActions.Where(a => a.DueAt <= now);
                if (cursor is { } after) query = query.Where(a => a.AgentSessionId.CompareTo(after) > 0);
                var sessions = await query.Select(a => a.AgentSessionId).Distinct().OrderBy(id => id).Take(PageSize).ToListAsync(ct);
                // Discovery may wait for capacity; ingestion only offers nonblocking hints.
                // Retaining this bounded page avoids losing overflow IDs on a frozen clock.
                foreach (var sessionId in sessions) await _hints.Writer.WriteAsync(sessionId, ct);
                cursor = sessions.Count == PageSize ? sessions[^1] : null;
                if (cursor is not null) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Diagnostic(ex, "Action discovery failed", Guid.Empty); }
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var backstop = Task.Delay(TimeSpan.FromSeconds(1), clock, wait.Token);
            var overflow = _scan.Reader.ReadAsync(wait.Token).AsTask();
            await Task.WhenAny(backstop, overflow);
            await wait.CancelAsync();
            try { await Task.WhenAll(backstop, overflow); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task WorkAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var sessionId in _hints.Reader.ReadAllAsync(ct))
            {
                if (!_owners.TryAdd(sessionId, 0)) continue;
                var more = false;
                try { more = await DrainPageAsync(sessionId, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) { Diagnostic(ex, "Action session execution failed", sessionId); }
                finally { _owners.TryRemove(sessionId, out _); }
                // Offer the continuation only after releasing ownership. Otherwise a free
                // worker can consume and discard this session's sole second-page hint.
                if (more) Hint(sessionId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal async Task<bool> DrainPageAsync(Guid sessionId, CancellationToken ct)
    {
        await ObserveDebtAsync(sessionId, "start", ct);
        try { return await DrainCoreAsync(sessionId, ct); }
        finally { await ObserveDebtAsync(sessionId, "end", CancellationToken.None); }
    }

    private async Task<bool> DrainCoreAsync(Guid sessionId, CancellationToken ct)
    {
        for (var i = 0; i < PageSize; i++)
        {
            ct.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Route all committed work before considering any idle request. A failed earlier
            // action holds this session's order, without occupying a worker between retries.
            // Channel debt holds only later channel debt to the same destination (CARD-1434): the
            // earliest due row per conversation key runs; a null key is every destination.
            var now = clock.GetUtcNow().UtcDateTime;
            var action = await db.SessionRuntimeActions.Where(a => a.AgentSessionId == sessionId
                    && (a.TriggerKind != ChannelReplyDebt || (a.DueAt <= now
                        && !db.SessionRuntimeActions.Any(b => b.AgentSessionId == sessionId
                            && b.TriggerKind == ChannelReplyDebt && b.Sequence < a.Sequence
                            && (b.ConversationKey == null || a.ConversationKey == null
                                || b.ConversationKey == a.ConversationKey)))))
                .OrderBy(a => a.Phase >= QueuePhase).ThenBy(a => a.Sequence).ThenBy(a => a.TriggerKind)
                .FirstOrDefaultAsync(ct);
            if (action is null) return false;
            // A queue wakeup is coalescible only after all of its routing has completed.
            // New work must be able to replace a deferred wakeup before its retry is due.
            var newerQueueRequest = action.Phase == QueuePhase && action.FailureReason == DeferredReason
                && await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == sessionId
                    && a.Id != action.Id && a.Phase == QueuePhase
                    && (a.TurnBoundary || a.ManualCompactBoundary
                        || (a.TranscriptEntryId == null && a.TriggerKind != "catchup"))
                    && (a.Sequence > action.Sequence || (a.Sequence == action.Sequence
                        && (a.CreatedAt > action.CreatedAt
                            || (a.TriggerKind == "status" && action.TriggerKind != "status")))), ct);
            var superseded = newerQueueRequest
                && (action.TurnBoundary || action.ManualCompactBoundary || action.TriggerKind == "status");
            // A typed handoff must execute its own queue phase, never transfer its identity
            // to a wakeup. The same newer-request ordering can still make it eligible early.
            var recheckHandoff = newerQueueRequest && action.TranscriptEntryId is null
                && action.TriggerKind.StartsWith("review:", StringComparison.Ordinal);
            if (action.DueAt > clock.GetUtcNow().UtcDateTime && !superseded && !recheckHandoff) return false;
            ObserveAction(action);
            try
            {
                if (await ExecuteAsync(scope.ServiceProvider, db, action, superseded, ct) == ExecutionDisposition.Deferred)
                    return false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                var attempts = Math.Min(action.Attempts + 1, 31);
                var due = RetryAt(attempts);
                var reason = ex.GetType().Name;
                await db.SessionRuntimeActions.Where(a => a.Id == action.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Attempts, attempts)
                        .SetProperty(a => a.DueAt, due).SetProperty(a => a.FailureReason, reason), ct);
                Diagnostic(ex, "Action phase retained for retry", sessionId);
                if (action.TriggerKind != ChannelReplyDebt) return false;
                // Later replies to this destination wait for this one; they need no wakeup
                // before it is due. Same key predicate as head selection.
                var key = action.ConversationKey;
                await db.SessionRuntimeActions.Where(a => a.AgentSessionId == sessionId
                        && a.TriggerKind == ChannelReplyDebt && a.Sequence > action.Sequence && a.DueAt < due
                        && (key == null || a.ConversationKey == null || a.ConversationKey == key))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.DueAt, due), ct);
            }
        }
        return true;
    }

    internal static SessionRunnerTranscriptEvent RestoreTranscriptPayload(TranscriptEntry row, SessionRuntimeAction action) =>
        TranscriptUiProjection.Restore(row, action);

    private DateTime RetryAt(int attempts) => clock.GetUtcNow().UtcDateTime
        .AddSeconds(Math.Min(30, 1 << Math.Min(attempts - 1, 5)));

    private enum ExecutionDisposition { Continue, Deferred }

    private async Task<ExecutionDisposition> ExecuteAsync(IServiceProvider services, AppDbContext db, SessionRuntimeAction action,
        bool superseded, CancellationToken ct)
    {
        var row = action.TranscriptEntryId is { } id
            ? await db.TranscriptEntries.AsNoTracking().SingleAsync(t => t.Id == id, ct) : null;
        var runtime = services.GetRequiredService<AgentSessionRuntime>();
        if (action.TriggerKind == ChannelReplyDebt)
        {
            await runtime.ExecuteActionChannelReplyAsync(action.AgentSessionId, action.Sequence, ct);
            // CARD-1433: a debt row that carries late-confirmed sources publishes them too.
            if (action.LateConfirmedChannelMessageIds.Length > 0)
                await runtime.ExecuteActionLateConfirmAsync(action, ct);
            db.SessionRuntimeActions.Remove(action);
            await db.SaveChangesAsync(ct);
            return ExecutionDisposition.Continue;
        }
        var routed = action.Phase < QueuePhase;
        while (action.Phase < QueuePhase)
        {
            ct.ThrowIfCancellationRequested();
            if (action.Phase == ChannelReplyPhase && AgentSessionRuntime.RoutesReplies(action, row))
                await ExecuteChannelReplyPhaseAsync(runtime, db, action, ct);
            else await runtime.ExecuteActionPhaseAsync(action, row, ct);
            action.Phase++;
            action.Attempts = 0;
            action.FailureReason = null;
            await db.SaveChangesAsync(ct);
        }
        if (row is not null && !action.TurnBoundary
            && !action.ManualCompactBoundary
            || action.TriggerKind == "catchup")
        {
            db.SessionRuntimeActions.Remove(action);
            await db.SaveChangesAsync(ct);
            return ExecutionDisposition.Continue;
        }
        // Re-select after routing so an older deferred queue wakeup can observe this
        // replacement before the new request completes and disappears.
        if (routed) return ExecutionDisposition.Continue;
        // Re-query after routing: ingestion may have advanced while a downstream awaited.
        // Channel debt is not routing the queue waits for; the dispatcher resolves its
        // turn from stored rows, and replies keep their own order.
        if (await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == action.AgentSessionId
            && a.Id != action.Id && a.Phase < QueuePhase && a.TriggerKind != ChannelReplyDebt, ct))
            return ExecutionDisposition.Continue;
        var generation = await db.AgentSessions.Where(s => s.Id == action.AgentSessionId)
            .Select(s => (DateTime?)s.StartedAt).SingleOrDefaultAsync(ct);
        if (generation == action.AcceptedGeneration && action.Phase == QueuePhase)
        {
            var result = await runtime.ExecuteActionQueueAsync(action, superseded, ct);
            // The queue saves exact source IDs alongside the receipt verdict. Do not erase
            // that obligation with this context's older copy when acknowledging the phase.
            await db.Entry(action).ReloadAsync(ct);
            if (result.Deferred)
            {
                action.Attempts = Math.Min(action.Attempts + 1, 31);
                action.DueAt = RetryAt(action.Attempts);
                action.FailureReason = DeferredReason;
                await db.SaveChangesAsync(ct);
                // The newer wake remains behind this head. Yield after one failed recheck
                // so it cannot bypass the freshly persisted retry for the whole page.
                return ExecutionDisposition.Deferred;
            }
            action.LateConfirmedChannelMessageIds = action.LateConfirmedChannelMessageIds
                .Union(result.LateConfirmedChannelMessageIds).ToArray();
            action.Phase++;
            action.Attempts = 0;
            action.FailureReason = null;
            await db.SaveChangesAsync(ct);
        }
        if (action.Phase > QueuePhase)
            await ExecuteLateConfirmPhaseAsync(runtime, db, action, ct);
        db.SessionRuntimeActions.Remove(action);
        await db.SaveChangesAsync(ct);
        return ExecutionDisposition.Continue;
    }

    /// <summary>
    /// Runs this action's channel reply, unless an earlier reply to the same destination is
    /// still owed. A failed or waiting reply becomes channel debt keyed by its destination,
    /// saved with this action's phase advance, so the later phases (review, settlement,
    /// refinement confirmation, catch-up) still run. A trigger with no publishable boundary
    /// publishes nothing and owes nothing (CARD-1434 D-2).
    /// </summary>
    private async Task ExecuteChannelReplyPhaseAsync(AgentSessionRuntime runtime, AppDbContext db,
        SessionRuntimeAction action, CancellationToken ct)
    {
        var route = await runtime.ResolveActionChannelRouteAsync(action.AgentSessionId, action.Sequence, ct);
        if (route is null)
        {
            await runtime.ExecuteActionChannelReplyAsync(action.AgentSessionId, action.Sequence, ct);
            return;
        }
        var key = route.ConversationKey;
        var owed = await db.SessionRuntimeActions.Where(a => a.AgentSessionId == action.AgentSessionId
                && a.TriggerKind == ChannelReplyDebt && a.Sequence < action.Sequence
                && (key == null || a.ConversationKey == null || a.ConversationKey == key))
            .MaxAsync(a => (DateTime?)a.DueAt, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        Exception? failure = null;
        if (owed is null)
        {
            try
            {
                await runtime.ExecuteActionChannelReplyAsync(action.AgentSessionId, action.Sequence, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failure = ex; }
        }
        if (await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == action.AgentSessionId
            && a.Sequence == action.Sequence && a.TriggerKind == ChannelReplyDebt, ct)) return;
        db.SessionRuntimeActions.Add(new SessionRuntimeAction { Id = Guid.NewGuid(),
            AgentSessionId = action.AgentSessionId, AcceptedGeneration = action.AcceptedGeneration,
            AcceptedTaskId = action.AcceptedTaskId, AcceptedTaskAttempt = action.AcceptedTaskAttempt,
            Sequence = action.Sequence, ProducerSequence = action.ProducerSequence,
            TriggerKind = ChannelReplyDebt, Phase = ChannelReplyPhase, CreatedAt = now,
            DueAt = failure is null ? (owed > now ? owed.Value : now) : RetryAt(1),
            Attempts = failure is null ? 0 : 1, FailureReason = failure?.GetType().Name,
            ConversationKey = key });
        if (failure is not null) Diagnostic(failure, "Channel reply retained as debt", action.AgentSessionId);
    }

    /// <summary>
    /// CARD-1433: publishes this action's late-confirmed channel sources, unless an earlier
    /// reply to their destination is still owed at or below this sequence. A waiting or failed
    /// publication becomes channel debt at this sequence carrying the source ids (merged into
    /// the phase-4 row when one exists), saved with the action's removal, so later queue
    /// actions are not held behind it. Late-confirmed replies still publish after any earlier
    /// owed reply to the same destination, never past it.
    /// </summary>
    private async Task ExecuteLateConfirmPhaseAsync(AgentSessionRuntime runtime, AppDbContext db,
        SessionRuntimeAction action, CancellationToken ct)
    {
        var ids = action.LateConfirmedChannelMessageIds;
        if (ids.Length == 0) return;
        var keys = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == action.AgentSessionId && ids.Contains(m.Id))
            .Select(m => m.ConversationKey).Distinct().ToListAsync(ct);
        var key = keys.Count == 1 ? keys[0] : null;
        var owed = await db.SessionRuntimeActions.Where(a => a.AgentSessionId == action.AgentSessionId
                && a.TriggerKind == ChannelReplyDebt && a.Sequence <= action.Sequence
                && (key == null || a.ConversationKey == null || a.ConversationKey == key))
            .MaxAsync(a => (DateTime?)a.DueAt, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        Exception? failure = null;
        if (owed is null)
        {
            try
            {
                await runtime.ExecuteActionLateConfirmAsync(action, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { failure = ex; }
        }
        var debt = await db.SessionRuntimeActions.SingleOrDefaultAsync(a => a.AgentSessionId == action.AgentSessionId
            && a.Sequence == action.Sequence && a.TriggerKind == ChannelReplyDebt, ct);
        if (debt is not null)
        {
            debt.LateConfirmedChannelMessageIds = debt.LateConfirmedChannelMessageIds.Union(ids).ToArray();
            if (debt.ConversationKey != key) debt.ConversationKey = null;
        }
        else
            db.SessionRuntimeActions.Add(new SessionRuntimeAction { Id = Guid.NewGuid(),
                AgentSessionId = action.AgentSessionId, AcceptedGeneration = action.AcceptedGeneration,
                AcceptedTaskId = action.AcceptedTaskId, AcceptedTaskAttempt = action.AcceptedTaskAttempt,
                Sequence = action.Sequence, ProducerSequence = action.ProducerSequence,
                TriggerKind = ChannelReplyDebt, Phase = ChannelReplyPhase, CreatedAt = now,
                DueAt = failure is null ? (owed > now ? owed.Value : now) : RetryAt(1),
                Attempts = failure is null ? 0 : 1, FailureReason = failure?.GetType().Name,
                ConversationKey = key, LateConfirmedChannelMessageIds = ids });
        if (failure is not null) Diagnostic(failure, "Late-confirm publication retained as debt", action.AgentSessionId);
    }

    private void Diagnostic(Exception exception, string message, Guid sessionId)
    {
        try { logger.LogWarning("{ActionMessage} for session {SessionId}; failure {FailureType}", message, sessionId, exception.GetType().Name); }
        catch { /* Diagnostics cannot change admission or ownership. */ }
    }

    private async Task ObserveDebtAsync(Guid sessionId, string transition, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var debt = await db.SessionRuntimeActions.Where(a => a.AgentSessionId == sessionId)
                .GroupBy(a => a.AgentSessionId).Select(g => new { Count = g.Count(), Frontier = g.Max(a => a.Sequence) })
                .SingleOrDefaultAsync(ct);
            logger.LogInformation("Action worker {Transition} session {SessionId}; pending {PendingCount}, frontier {Frontier}",
                transition, sessionId, debt?.Count ?? 0, debt?.Frontier ?? 0L);
        }
        catch { /* Observability has no completion authority. */ }
    }

    private void ObserveAction(SessionRuntimeAction action)
    {
        try { logger.LogInformation("Action {ActionId} session {SessionId}; phase {Phase}, age {AgeSeconds}s, frontier {Frontier}, attempts {Attempts}",
            action.Id, action.AgentSessionId, action.Phase, Math.Max(0, (clock.GetUtcNow().UtcDateTime - action.CreatedAt).TotalSeconds),
            action.Sequence, action.Attempts); }
        catch { }
    }
}

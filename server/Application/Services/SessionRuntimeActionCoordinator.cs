using System.Collections.Concurrent;
using System.Threading.Channels;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
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
        return false;
    }

    internal static SessionRuntimeAction Admit(AppDbContext db, TranscriptEntry row,
        SessionRunnerTranscriptEvent source, DateTime generation, bool boundary, DateTime now)
    {
        var action = new SessionRuntimeAction { Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId,
            TranscriptEntryId = row.Id, AcceptedGeneration = generation, Sequence = row.Sequence,
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
            db.SessionRuntimeActions.Add(new SessionRuntimeAction { Id = Guid.NewGuid(), AgentSessionId = sessionId,
                AcceptedGeneration = generation, Sequence = sequence, TriggerKind = kind,
                Phase = kind == "status" ? QueuePhase : 1, CreatedAt = clock.GetUtcNow().UtcDateTime,
                DueAt = clock.GetUtcNow().UtcDateTime });
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
                try { await DrainPageAsync(sessionId, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex) { Diagnostic(ex, "Action session execution failed", sessionId); }
                finally { _owners.TryRemove(sessionId, out _); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal async Task DrainPageAsync(Guid sessionId, CancellationToken ct)
    {
        for (var i = 0; i < PageSize; i++)
        {
            ct.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Route all committed work before considering any idle request. A failed earlier
            // action holds this session's order, without occupying a worker between retries.
            var action = await db.SessionRuntimeActions.Where(a => a.AgentSessionId == sessionId)
                .OrderBy(a => a.Phase >= QueuePhase).ThenBy(a => a.Sequence).ThenBy(a => a.TriggerKind)
                .FirstOrDefaultAsync(ct);
            if (action is null || action.DueAt > clock.GetUtcNow().UtcDateTime) return;
            try
            {
                await ExecuteAsync(scope.ServiceProvider, db, action, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                var attempts = Math.Min(action.Attempts + 1, 31);
                var due = clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(30, 1 << Math.Min(attempts - 1, 5)));
                var reason = ex.GetType().Name;
                await db.SessionRuntimeActions.Where(a => a.Id == action.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Attempts, attempts)
                        .SetProperty(a => a.DueAt, due).SetProperty(a => a.FailureReason, reason), ct);
                Diagnostic(ex, "Action phase retained for retry", sessionId);
                return;
            }
        }
        Hint(sessionId); // yield ownership at the page boundary
    }

    internal static SessionRunnerTranscriptEvent RestoreTranscriptPayload(TranscriptEntry row, SessionRuntimeAction action) =>
        TranscriptUiProjection.Restore(row, action);

    private async Task ExecuteAsync(IServiceProvider services, AppDbContext db, SessionRuntimeAction action, CancellationToken ct)
    {
        var row = action.TranscriptEntryId is { } id
            ? await db.TranscriptEntries.AsNoTracking().SingleAsync(t => t.Id == id, ct) : null;
        var runtime = services.GetRequiredService<AgentSessionRuntime>();
        while (action.Phase < QueuePhase)
        {
            ct.ThrowIfCancellationRequested();
            await runtime.ExecuteActionPhaseAsync(action, row, ct);
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
            return;
        }
        // Re-query after routing: ingestion may have advanced while a downstream awaited.
        if (await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == action.AgentSessionId
            && a.Id != action.Id && a.Phase < QueuePhase, ct)) return;
        var generation = await db.AgentSessions.Where(s => s.Id == action.AgentSessionId)
            .Select(s => (DateTime?)s.StartedAt).SingleOrDefaultAsync(ct);
        if (generation == action.AcceptedGeneration && action.Phase == QueuePhase)
        {
            var result = await runtime.ExecuteActionQueueAsync(action, ct);
            // The queue saves exact source IDs alongside the receipt verdict. Do not erase
            // that obligation with this context's older copy when acknowledging the phase.
            await db.Entry(action).ReloadAsync(ct);
            if (result.Deferred)
            {
                action.DueAt = clock.GetUtcNow().UtcDateTime.AddSeconds(1);
                await db.SaveChangesAsync(ct);
                return;
            }
            action.LateConfirmedChannelMessageIds = action.LateConfirmedChannelMessageIds
                .Union(result.LateConfirmedChannelMessageIds).ToArray();
            action.Phase++;
            await db.SaveChangesAsync(ct);
        }
        if (action.Phase > QueuePhase) await runtime.ExecuteActionLateConfirmAsync(action, ct);
        db.SessionRuntimeActions.Remove(action);
        await db.SaveChangesAsync(ct);
    }

    private void Diagnostic(Exception exception, string message, Guid sessionId)
    {
        try { logger.LogWarning(exception, "{ActionMessage} for session {SessionId}", message, sessionId); }
        catch { /* Diagnostics cannot change admission or ownership. */ }
    }
}

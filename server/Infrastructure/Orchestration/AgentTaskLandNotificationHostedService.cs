using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Boot scan and periodic backstop. Row failures cannot prevent later pages running.</summary>
public sealed class AgentTaskLandNotificationHostedService(IServiceScopeFactory scopes,
    ILogger<AgentTaskLandNotificationHostedService> logger) : BackgroundService
{
    /// <summary>
    /// Process-local. A restart drops it, so the next pass reconciles each AwaitingReceipt note
    /// once and then parks it again. It is not a column and it does not change a direct reconcile.
    /// </summary>
    private readonly RescanPark _rescanPark = new();

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
        var stamps = await LoadRescanStampsAsync(_rescanPark.SessionIds(), stoppingToken);
        var parked = new List<(Guid NoteId, Guid SessionId)>();
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
            // CARD-1769. Canceled is absent from that page. A linked AwaitingReceipt note whose
            // session stamp still matches stays parked until its backoff. The stamp is this
            // pass's read: a status, start, or prompt-sequence change is reconciled here, and
            // anything else that leaves the note AwaitingReceipt waits out the backoff.
            var now = (scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var ids = new List<Guid>(128);
            var page = cursor is Guid after
                ? LandNotificationReconcileQueries.DueNoteIdsAfter(db, now, after)
                : LandNotificationReconcileQueries.DueNoteIds(db, now);
            await foreach (var id in page.WithCancellation(stoppingToken))
                ids.Add(id);
            foreach (var id in ids)
            {
                if (_rescanPark.Unchanged(id, now, stamps))
                    continue;
                try
                {
                    await using var rowScope = scopes.CreateAsyncScope();
                    var rowDb = rowScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await rowScope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                        .ReconcileAsync(id, stoppingToken, busySessions);
                    var tracked = rowDb.AgentTaskLandNotifications.Local.FirstOrDefault(n => n.Id == id);
                    if (tracked is { State: LandNotificationState.AwaitingReceipt, ParentSessionId: Guid parent })
                        parked.Add((id, parent));
                    else
                        _rescanPark.Forget(id);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Land notification {NotificationId} remains unresolved", id);
                    _rescanPark.Forget(id);
                }
            }
            if (ids.Count < 128) break;
            cursor = ids[^1];
        }

        await RememberRescansAsync(parked, stoppingToken);
    }

    /// <summary>CARD-1769. The rescan park's cap. A note past its backoff is reconciled on the next pass.</summary>
    internal void ExpireRescanPark(Guid noteId) => _rescanPark.Expire(noteId);

    private async Task<Dictionary<Guid, SessionRescanStamp>> LoadRescanStampsAsync(Guid[] sessionIds, CancellationToken ct)
    {
        if (sessionIds.Length == 0)
            return new();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await LoadStampsAsync(db, sessionIds, ct);
    }

    private async Task RememberRescansAsync(List<(Guid NoteId, Guid SessionId)> parked, CancellationToken ct)
    {
        if (parked.Count == 0)
            return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = (scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var stamps = await LoadStampsAsync(db, parked.Select(row => row.SessionId).Distinct().ToArray(), ct);
        foreach (var (noteId, sessionId) in parked)
        {
            if (stamps.TryGetValue(sessionId, out var stamp))
                _rescanPark.Remember(noteId, sessionId, stamp, now);
            else
                _rescanPark.Forget(noteId);
        }
    }

    /// <summary>
    /// One round trip for the session row and one for the newest receipt prompt. Parameters are
    /// session ids. A note id in that list would look like a note load to the rescan witness.
    /// </summary>
    private static async Task<Dictionary<Guid, SessionRescanStamp>> LoadStampsAsync(
        AppDbContext db, Guid[] sessionIds, CancellationToken ct)
    {
        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Status, s.StartedAt })
            .ToListAsync(ct);
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => sessionIds.Contains(t.AgentSessionId)
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { SessionId = g.Key, Sequence = g.Max(t => t.Sequence) })
            .ToListAsync(ct);
        var sequences = prompts.ToDictionary(row => row.SessionId, row => row.Sequence);
        return sessions.ToDictionary(
            row => row.Id,
            row => new SessionRescanStamp(row.Status, row.StartedAt, sequences.GetValueOrDefault(row.Id)));
    }

    private readonly record struct SessionRescanStamp(SessionStatus Status, DateTime StartedAt, long PromptSequence);

    /// <summary>
    /// CARD-1769. After a reconcile leaves a note <see cref="LandNotificationState.AwaitingReceipt"/>,
    /// the next hosted passes skip it while two things are still true: the backoff has not elapsed,
    /// and the target session's status, start, and max UserPrompt/QueuedUserPrompt sequence still
    /// match the stamp taken after that reconcile. The first backoff is one minute and doubles
    /// until <see cref="MaximumBackoff"/>.
    /// A database-visible status, start, or prompt-sequence change is reconciled on the next hosted
    /// pass (the five-second period, plus whatever remains of a pass already in flight, plus that
    /// page's clock read). A queue cancel, a queue-identity mismatch, or a receipt that exists only
    /// on the runner until catch-up runs waits for the backoff. That wait is at most five minutes.
    /// A missing session is not a match, so the note is reconciled.
    /// </summary>
    private sealed class RescanPark
    {
        internal static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);
        internal static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

        private sealed class Entry
        {
            public Guid SessionId { get; init; }
            public SessionStatus Status { get; init; }
            public DateTime StartedAt { get; init; }
            public long PromptSequence { get; init; }
            public DateTime ParkedUntil { get; set; }
            public int Observations { get; init; }
        }

        private readonly Dictionary<Guid, Entry> _entries = new();

        internal Guid[] SessionIds()
        {
            var ids = new HashSet<Guid>();
            foreach (var entry in _entries.Values)
                ids.Add(entry.SessionId);
            return [.. ids];
        }

        internal void Expire(Guid noteId)
        {
            if (_entries.TryGetValue(noteId, out var entry))
                entry.ParkedUntil = DateTime.MinValue;
        }

        internal void Forget(Guid noteId) => _entries.Remove(noteId);

        internal bool Unchanged(Guid noteId, DateTime now, IReadOnlyDictionary<Guid, SessionRescanStamp> stamps)
        {
            if (!_entries.TryGetValue(noteId, out var entry))
                return false;
            if (now >= entry.ParkedUntil)
                return false;
            if (!stamps.TryGetValue(entry.SessionId, out var stamp))
                return false;
            return stamp.Status == entry.Status
                && stamp.PromptSequence == entry.PromptSequence
                && SessionGeneration.Equal(stamp.StartedAt, entry.StartedAt);
        }

        internal void Remember(Guid noteId, Guid sessionId, SessionRescanStamp stamp, DateTime now)
        {
            var observations = _entries.TryGetValue(noteId, out var prior) ? prior.Observations + 1 : 1;
            _entries[noteId] = new Entry
            {
                SessionId = sessionId,
                Status = stamp.Status,
                StartedAt = stamp.StartedAt,
                PromptSequence = stamp.PromptSequence,
                ParkedUntil = now.Add(Backoff(observations)),
                Observations = observations,
            };
        }

        private static TimeSpan Backoff(int observations)
        {
            var backoff = InitialBackoff;
            for (var i = 1; i < observations && backoff < MaximumBackoff; i++)
            {
                var doubled = TimeSpan.FromTicks(backoff.Ticks * 2);
                backoff = doubled <= MaximumBackoff ? doubled : MaximumBackoff;
            }
            return backoff;
        }
    }
}

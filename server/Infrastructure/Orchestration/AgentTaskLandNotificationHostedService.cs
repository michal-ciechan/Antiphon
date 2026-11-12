using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
                await ExpireAgedQueueRowsIfDueAsync(stoppingToken);
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
    /// Debug 3b0adade (CARD-0747 follow-up). How often the loop applies <see cref="AgedQueueRowExpiry"/>:
    /// the first pass after boot runs it, then once an hour, or after a minute when a held queue lock
    /// left a session's rows for later.
    /// </summary>
    internal static readonly TimeSpan QueueRowExpiryPeriod = TimeSpan.FromHours(1);
    internal static readonly TimeSpan QueueRowExpiryBusyRetry = TimeSpan.FromMinutes(1);
    private DateTime _nextQueueRowExpiryAt = DateTime.MinValue;

    /// <summary>
    /// Cancels never-typed Pending Delegation and Check rows of terminal tasks that no open land note
    /// owns, once they are older than <c>LandNoteReceiptExpiryDays</c> (zero or negative turns it off,
    /// as it does the note rule). The note scan above only reaches rows a note links.
    /// </summary>
    private async Task ExpireAgedQueueRowsIfDueAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            if (now < _nextQueueRowExpiryAt) return;
            _nextQueueRowExpiryAt = now + QueueRowExpiryPeriod;
            var days = (services.GetService<IOptions<SupervisionSettings>>()?.Value ?? new SupervisionSettings())
                .DeliveryVerification.LandNoteReceiptExpiryDays;
            if (days <= 0) return;
            var result = await new AgedQueueRowExpiry(services.GetRequiredService<AppDbContext>(),
                    services.GetRequiredService<SessionMessageQueueService>())
                .ExpireAsync(now.AddDays(-days), apply: true, AgentTaskLandNotificationService.ScanQueueLockWait, ct);
            if (result.Outcomes.GetValueOrDefault(nameof(AgedQueueRowExpiry.Outcome.Busy)) > 0)
                _nextQueueRowExpiryAt = now + QueueRowExpiryBusyRetry;
            if (result.Scanned > 0)
                logger.LogInformation(
                    "Aged queue-row expiry: {Scanned} scanned, {Canceled} canceled ({Outcomes}), {Failures} failed",
                    result.Scanned, result.Outcomes.GetValueOrDefault(nameof(AgedQueueRowExpiry.Outcome.Canceled)),
                    string.Join(", ", result.Outcomes.Select(o => $"{o.Key}={o.Value}")), result.Failures);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { logger.LogWarning(ex, "Aged queue-row expiry failed"); }
    }

    /// <summary>
    /// CARD-1769. Fired after one note's reconcile. That note's park stamp was read before the
    /// reconcile, so a session or queue change made here is visible on the next pass.
    /// </summary>
    internal const string RescanReconciledBoundary = "land-rescan-reconciled";

    /// <summary>
    /// One land-notification pass, in id order. The hosted loop and the CARD-1769 measurement
    /// share it. <paramref name="busySessions"/> is the caller's queue-lock set for this pass.
    /// </summary>
    internal async Task ScanLandNotificationsOnceAsync(CancellationToken stoppingToken, ISet<Guid>? busySessions = null)
    {
        var wake = await LoadParkedStampsAsync(stoppingToken);
        await using var boundaryScope = scopes.CreateAsyncScope();
        var rescanBoundary = boundaryScope.ServiceProvider.GetService<LandDeliveryBoundary>();
        // CARD-1773. One pull set and one seen set for the whole pass, including later pages.
        var transcriptPulls = new HashSet<Guid>();
        var seen = new HashSet<Guid>();
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
            // CARD-1769. Canceled is absent from that page. A linked AwaitingReceipt note stays
            // parked until its backoff when the stamp still matches. The stamp compared here was
            // read at the start of this pass. The stamp recorded for a note this pass reconciles
            // is read before that page's reconciles.
            var now = (scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var ids = new List<Guid>(128);
            var page = cursor is Guid after
                ? LandNotificationReconcileQueries.DueNoteIdsAfter(db, now, after)
                : LandNotificationReconcileQueries.DueNoteIds(db, now);
            await foreach (var id in page.WithCancellation(stoppingToken))
                ids.Add(id);
            foreach (var id in ids)
                seen.Add(id);
            var pending = new List<Guid>(ids.Count);
            foreach (var id in ids)
            {
                if (!_rescanPark.Unchanged(id, now, wake.Sessions, wake.Queues))
                    pending.Add(id);
            }

            var pre = pending.Count == 0
                ? PreReconcile.Empty
                : await LoadPreReconcileStampsAsync(pending, stoppingToken);
            foreach (var id in pending)
            {
                try
                {
                    await using var rowScope = scopes.CreateAsyncScope();
                    var rowDb = rowScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await rowScope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                        .ReconcileAsync(id, stoppingToken, busySessions, transcriptPulls);
                    var tracked = rowDb.AgentTaskLandNotifications.Local.FirstOrDefault(n => n.Id == id);
                    var observedSession = Guid.Empty;
                    if (tracked is { State: LandNotificationState.AwaitingReceipt, ParentSessionId: Guid parent })
                    {
                        observedSession = parent;
                        var parkedAt = (rowScope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System)
                            .GetUtcNow().UtcDateTime;
                        RememberPreReconcile(id, parent, tracked.QueueMessageId, pre, parkedAt);
                    }
                    else
                        _rescanPark.Forget(id);
                    if (rescanBoundary is not null)
                        await rescanBoundary.ReachedAsync(RescanReconciledBoundary, id, observedSession, stoppingToken);
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

        // A canceled pass has not seen every later page, so it must not drop entries for them.
        if (!stoppingToken.IsCancellationRequested)
            _rescanPark.Retain(seen);
    }

    /// <summary>CARD-1769. The rescan park's cap. A note past its backoff is reconciled on the next pass.</summary>
    internal void ExpireRescanPark(Guid noteId) => _rescanPark.Expire(noteId);

    private async Task<(Dictionary<Guid, SessionRescanStamp> Sessions, Dictionary<Guid, QueueRescanStamp> Queues)> LoadParkedStampsAsync(
        CancellationToken ct)
    {
        var sessionIds = _rescanPark.SessionIds();
        var queueIds = _rescanPark.QueueMessageIds();
        if (sessionIds.Length == 0 && queueIds.Length == 0)
            return (new(), new());
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await LoadStampsAsync(db, sessionIds, ct), await LoadQueueStampsAsync(db, queueIds, ct));
    }

    private async Task<PreReconcile> LoadPreReconcileStampsAsync(List<Guid> noteIds, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => noteIds.Contains(n.Id))
            .Select(n => new { n.Id, n.ParentSessionId, n.QueueMessageId })
            .ToListAsync(ct);
        var notes = new Dictionary<Guid, NoteIdentity>(rows.Count);
        foreach (var row in rows)
            notes[row.Id] = new NoteIdentity(row.ParentSessionId, row.QueueMessageId);
        var sessionIds = rows.Select(row => row.ParentSessionId).OfType<Guid>().Distinct().ToArray();
        var queueIds = rows.Select(row => row.QueueMessageId).OfType<Guid>().Distinct().ToArray();
        return new PreReconcile(
            notes,
            await LoadStampsAsync(db, sessionIds, ct),
            await LoadQueueStampsAsync(db, queueIds, ct));
    }

    /// <summary>
    /// Park with the stamp read before this page's reconciles. A parent or queue id that reconcile
    /// itself changed has no pre-reconcile stamp, so the note is left unparked and the next pass
    /// reads it.
    /// </summary>
    private void RememberPreReconcile(Guid noteId, Guid parent, Guid? queueId, PreReconcile pre, DateTime parkedAt)
    {
        if (!pre.Notes.TryGetValue(noteId, out var prior)
            || prior.SessionId != parent
            || prior.QueueMessageId != queueId
            || !pre.Sessions.TryGetValue(parent, out var sessionStamp))
        {
            _rescanPark.Forget(noteId);
            return;
        }

        QueueRescanStamp? queueStamp = null;
        if (queueId is Guid queued)
        {
            if (!pre.Queues.TryGetValue(queued, out var loaded))
            {
                // CARD-1773. The keyed row is gone. Count the miss; park on the session stamp
                // only after MissingQueueParkAfter consecutive misses.
                _rescanPark.NoteQueueMissing(noteId, parent, queued, sessionStamp, parkedAt);
                return;
            }

            queueStamp = loaded;
        }

        _rescanPark.Remember(noteId, parent, queueId, sessionStamp, queueStamp, parkedAt);
    }

    /// <summary>
    /// One round trip for the session row and one for the newest receipt prompt. Parameters are
    /// session ids. A note id in that list would look like a note load to the rescan witness.
    /// </summary>
    private static async Task<Dictionary<Guid, SessionRescanStamp>> LoadStampsAsync(
        AppDbContext db, Guid[] sessionIds, CancellationToken ct)
    {
        if (sessionIds.Length == 0)
            return new();
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

    /// <summary>
    /// One read of the parked queue rows. Parameters are queue message ids, not note ids.
    /// </summary>
    private static async Task<Dictionary<Guid, QueueRescanStamp>> LoadQueueStampsAsync(
        AppDbContext db, Guid[] messageIds, CancellationToken ct)
    {
        if (messageIds.Length == 0)
            return new();
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(message => messageIds.Contains(message.Id))
            .Select(message => new
            {
                message.Id,
                message.Status,
                message.DeliveryAttempts,
                message.DeliveryVerdict,
                message.CanceledAt,
            })
            .ToListAsync(ct);
        return rows.ToDictionary(
            row => row.Id,
            row => new QueueRescanStamp(row.Status, row.DeliveryAttempts, row.DeliveryVerdict, row.CanceledAt));
    }

    private readonly record struct SessionRescanStamp(SessionStatus Status, DateTime StartedAt, long PromptSequence);

    private readonly record struct QueueRescanStamp(
        QueuedMessageStatus Status, int DeliveryAttempts, DeliveryVerdict? Verdict, DateTime? CanceledAt);

    private readonly record struct NoteIdentity(Guid? SessionId, Guid? QueueMessageId);

    private sealed class PreReconcile(
        Dictionary<Guid, NoteIdentity> notes,
        Dictionary<Guid, SessionRescanStamp> sessions,
        Dictionary<Guid, QueueRescanStamp> queues)
    {
        public static PreReconcile Empty { get; } = new(new(), new(), new());
        public Dictionary<Guid, NoteIdentity> Notes { get; } = notes;
        public Dictionary<Guid, SessionRescanStamp> Sessions { get; } = sessions;
        public Dictionary<Guid, QueueRescanStamp> Queues { get; } = queues;
    }

    /// <summary>
    /// CARD-1769. After a reconcile leaves a note <see cref="LandNotificationState.AwaitingReceipt"/>,
    /// the next hosted passes skip it while the backoff has not elapsed and the stamp still matches.
    /// The stamp is the target session's status, start, and max UserPrompt/QueuedUserPrompt sequence,
    /// plus the queue row's status, delivery attempts, delivery verdict, and canceled-at. It is read
    /// before that page's reconciles and remembered as that read. Already-parked notes are compared
    /// with the session and prompt reads plus one <c>Id IN (parked queue ids)</c> read at the start of the pass.
    /// The first backoff is one minute and doubles until <see cref="MaximumBackoff"/>.
    /// A database-visible change to any stamped field is reconciled on the next hosted pass (the
    /// five-second period, plus whatever remains of a pass already in flight, plus that pass's stamp
    /// read). A receipt that exists only on the runner, with none of those fields changed, waits for
    /// the backoff. That wait is at most five minutes. A missing session row is not a match, so the
    /// note is reconciled. A missing queue row is reconciled until <see cref="MissingQueueParkAfter"/>
    /// consecutive misses, then parked on the session stamp while the row stays absent. A completed
    /// pass drops entries whose notes were not on any due page.
    /// </summary>
    private sealed class RescanPark
    {
        internal static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);
        internal static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

        /// <summary>CARD-1773. Consecutive reconciles that find the keyed queue row gone, then park.</summary>
        internal const int MissingQueueParkAfter = 3;

        private sealed class Entry
        {
            public Guid SessionId { get; init; }
            public SessionStatus Status { get; init; }
            public DateTime StartedAt { get; init; }
            public long PromptSequence { get; init; }
            public Guid? QueueMessageId { get; init; }
            public QueuedMessageStatus QueueStatus { get; init; }
            public int DeliveryAttempts { get; init; }
            public DeliveryVerdict? DeliveryVerdict { get; init; }
            public DateTime? CanceledAt { get; init; }
            public bool QueueMissing { get; init; }
            public int QueueMisses { get; init; }
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

        internal Guid[] QueueMessageIds()
        {
            var ids = new HashSet<Guid>();
            foreach (var entry in _entries.Values)
            {
                if (entry.QueueMessageId is Guid id)
                    ids.Add(id);
            }
            return [.. ids];
        }

        internal void Expire(Guid noteId)
        {
            if (_entries.TryGetValue(noteId, out var entry))
                entry.ParkedUntil = DateTime.MinValue;
        }

        internal void Forget(Guid noteId) => _entries.Remove(noteId);

        /// <summary>Drop entries whose notes were absent from a completed due-page pass.</summary>
        internal void Retain(IReadOnlySet<Guid> seen)
        {
            if (seen.Count == 0)
            {
                _entries.Clear();
                return;
            }

            List<Guid>? drop = null;
            foreach (var id in _entries.Keys)
            {
                if (!seen.Contains(id))
                    (drop ??= []).Add(id);
            }

            if (drop is null)
                return;
            foreach (var id in drop)
                _entries.Remove(id);
        }

        internal bool Unchanged(
            Guid noteId,
            DateTime now,
            IReadOnlyDictionary<Guid, SessionRescanStamp> sessions,
            IReadOnlyDictionary<Guid, QueueRescanStamp> queues)
        {
            if (!_entries.TryGetValue(noteId, out var entry))
                return false;
            if (now >= entry.ParkedUntil)
                return false;
            if (!sessions.TryGetValue(entry.SessionId, out var session))
                return false;
            if (session.Status != entry.Status
                || session.PromptSequence != entry.PromptSequence
                || !SessionGeneration.Equal(session.StartedAt, entry.StartedAt))
                return false;
            if (entry.QueueMessageId is not Guid queueId)
                return true;
            // Still missing stays parked. The row coming back is a change.
            if (entry.QueueMissing)
                return !queues.ContainsKey(queueId);
            if (!queues.TryGetValue(queueId, out var queue))
                return false;
            return queue.Status == entry.QueueStatus
                && queue.DeliveryAttempts == entry.DeliveryAttempts
                && queue.Verdict == entry.DeliveryVerdict
                && SameCanceledAt(queue.CanceledAt, entry.CanceledAt);
        }

        internal void Remember(
            Guid noteId,
            Guid sessionId,
            Guid? queueMessageId,
            SessionRescanStamp session,
            QueueRescanStamp? queue,
            DateTime now)
        {
            var observations = _entries.TryGetValue(noteId, out var prior) ? prior.Observations + 1 : 1;
            _entries[noteId] = new Entry
            {
                SessionId = sessionId,
                Status = session.Status,
                StartedAt = session.StartedAt,
                PromptSequence = session.PromptSequence,
                QueueMessageId = queueMessageId,
                QueueStatus = queue?.Status ?? default,
                DeliveryAttempts = queue?.DeliveryAttempts ?? 0,
                DeliveryVerdict = queue?.Verdict,
                CanceledAt = queue?.CanceledAt,
                QueueMissing = false,
                QueueMisses = 0,
                ParkedUntil = now.Add(Backoff(observations)),
                Observations = observations,
            };
        }

        /// <summary>
        /// Count one missing keyed row. Below <see cref="MissingQueueParkAfter"/> the entry stays
        /// due (parked-until is unset) so the next pass reconciles again. On the threshold the
        /// session stamp parks for <see cref="InitialBackoff"/>, not the doubled schedule.
        /// </summary>
        internal void NoteQueueMissing(
            Guid noteId, Guid sessionId, Guid queueMessageId, SessionRescanStamp session, DateTime now)
        {
            var misses = _entries.TryGetValue(noteId, out var prior) ? prior.QueueMisses + 1 : 1;
            var park = misses >= MissingQueueParkAfter;
            _entries[noteId] = new Entry
            {
                SessionId = sessionId,
                Status = session.Status,
                StartedAt = session.StartedAt,
                PromptSequence = session.PromptSequence,
                QueueMessageId = queueMessageId,
                QueueMissing = park,
                QueueMisses = misses,
                ParkedUntil = park ? now.Add(InitialBackoff) : DateTime.MinValue,
                Observations = prior?.Observations ?? 0,
            };
        }

        private static bool SameCanceledAt(DateTime? left, DateTime? right) =>
            left is null && right is null
            || left is not null && right is not null && SessionGeneration.Equal(left.Value, right.Value);

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

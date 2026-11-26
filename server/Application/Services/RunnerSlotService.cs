using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0653: list the seats a phone-home runner remembers, and force-release one or the orphans.
/// Release kills the process tree, drops the runner's durable manifest, and stamps the desktop
/// session so reconciliation does not treat it as still live.
/// </summary>
public static class RunnerSlotService
{
    public static bool OccupiesCapacity(string? status) =>
        !string.IsNullOrEmpty(status)
        && !status.Equals("Exited", StringComparison.OrdinalIgnoreCase)
        && !status.Equals("Failed", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An orphan has no live desktop session, or a live one with no owner task (Queued,
    /// Dispatched, Working or Blocked), unless that session is a warm pool delegate the
    /// process-release invariant is keeping on purpose. The flag is display and selection,
    /// never release authority.
    /// </summary>
    public static bool IsOrphan(bool desktopLive, bool openTask, bool pooledWarm) =>
        !pooledWarm && (!desktopLive || !openTask);

    public static async Task<RunnerSlotsDto> ListAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, CancellationToken ct)
    {
        var sessions = await directory.Resolve(runnerId).ListAsync(ct);
        var ids = sessions.Select(s => s.SessionId).ToArray();
        var desktop = await LoadDesktopAsync(db, ids, ct);
        var now = DateTime.UtcNow;
        var slots = sessions.Select(session =>
        {
            desktop.TryGetValue(session.SessionId, out var row);
            var occupies = OccupiesCapacity(session.Status);
            var orphan = IsOrphan(row.Live, row.OpenTaskId is not null, row.PooledWarm);
            var age = session.StartedAt == default
                ? 0
                : Math.Max(0, (int)(now - session.StartedAt.ToUniversalTime()).TotalSeconds);
            return new RunnerSlotDto(
                session.SessionId,
                session.Status,
                session.ExitReason.ToString(),
                age,
                occupies,
                orphan,
                session.Pid,
                session.VerificationBinding?.Backend,
                session.VerificationBinding?.ExecutionId,
                row.Status,
                row.OpenTaskId,
                ToPark(row.Park));
        }).ToArray();
        var capacity = directory.DeclaredCapacity(runnerId);
        return new RunnerSlotsDto(runnerId, capacity, slots.Count(s => s.OccupiesCapacity), slots);
    }

    public static async Task<RunnerSlotReleaseDto> ReleaseAsync(
        ISessionRunnerDirectory directory, AppDbContext db, string runnerId, Guid sessionId, string? reason,
        CancellationToken ct, ICollection<Guid>? intents = null)
    {
        var text = RequireReason(reason);
        await ReleaseOneAsync(directory, db, runnerId, sessionId, text, automatic: false, intents, ct);
        return new RunnerSlotReleaseDto(1, [sessionId]);
    }

    internal static Task ReleaseForGhostSweepAsync(
        ISessionRunnerDirectory directory, AppDbContext db, string runnerId, Guid sessionId, string reason,
        CancellationToken ct) =>
        ReleaseOneAsync(directory, db, runnerId, sessionId, reason, automatic: true, intents: null, ct);

    public static async Task<RunnerSlotReleaseDto> ReleaseOrphansAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, string? reason, CancellationToken ct,
        ICollection<Guid>? intents = null, TerminalRunnerSeatReleaseService? terminalSeats = null)
    {
        RequireReason(reason);
        // An orphan label is selection, never force authority. A missing/disabled coordinator
        // holds the sweep; explicit single-seat operator release keeps its separate contract.
        return terminalSeats is null ? new(0, [], Candidates: 0, Deferred: 0)
            : await terminalSeats.ReleaseOrphansAsync(runnerId, reason, ct);
    }

    /// <summary>
    /// Finish desktop audits whose runner release may have happened and whose save did not. This
    /// never kills: an intent is finished by auditing only, and only once the runner no longer
    /// lists the session. A seat the runner still holds stays pending, so a refused or stale
    /// release is never retried here. An intent from an orphan sweep is re-checked first: a
    /// seat a task claimed since the sweep is marked failed and its desktop row is left alone.
    /// Each intent is isolated, so one that throws (an unreachable runner, a failed save) does
    /// not block the rest; it stays pending for the next pass. Runs at startup, on the
    /// <c>RunnerSlotReconcileJob</c> schedule, and after a failed force-release request.
    /// The one exception is a <c>pending:kill-generation:</c> intent (CARD-0679 D-7): the kill was
    /// already decided by a failed launch, so it is sent, conditional on the recorded generation,
    /// once the runner resolves. The recovery pump also enqueues this job right after a reconnect.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ReconcilePendingReleasesAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, CancellationToken ct)
    {
        var pending = await PendingIntents(db)
            .OrderBy(incident => incident.CreatedAt)
            .Select(incident => new { incident.Id, incident.SessionId, incident.FailureReason, incident.Message })
            .ToListAsync(ct);
        var finished = new List<Guid>();
        foreach (var intent in pending)
        {
            try
            {
                if (intent.SessionId is not Guid sessionId)
                {
                    await MarkAsync(db, intent.Id, FailedPrefix + "the intent names no session", ct);
                    continue;
                }

                if (TryParseKillGeneration(intent.FailureReason!, out var killRunnerId, out var generation))
                {
                    if (await KillGenerationAsync(directory, db, intent.Id, killRunnerId, sessionId, generation, intent.Message, ct))
                        finished.Add(sessionId);
                    continue;
                }

                var (runnerId, fromOrphanSweep) = ParsePending(intent.FailureReason!);

                var listed = await directory.Resolve(runnerId).ListAsync(ct);
                if (listed.Any(session => session.SessionId == sessionId))
                    continue;
                if (fromOrphanSweep)
                {
                    var current = await LoadDesktopAsync(db, [sessionId], ct);
                    var row = current[sessionId];
                    if (!IsOrphan(row.Live, row.OpenTaskId is not null, row.PooledWarm))
                    {
                        await MarkAsync(db, intent.Id, FailedPrefix + "the seat was claimed after the orphan sweep", ct);
                        continue;
                    }
                }

                if (await FinishAsync(db, runnerId, sessionId, intent.Message, ct))
                    finished.Add(sessionId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return finished;
    }

    /// <summary>
    /// Where each of the given intents stands: <c>released</c> once audited, <c>failed</c> when
    /// refused or claimed, <c>closed</c> after an operator closes it, or <c>pending</c>.
    /// </summary>
    public static async Task<IReadOnlyList<RunnerSlotIntentOutcomeDto>> IntentOutcomesAsync(
        AppDbContext db, IReadOnlyCollection<Guid> intentIds, CancellationToken ct)
    {
        if (intentIds.Count == 0)
            return [];
        var rows = await db.AgentIncidents.AsNoTracking()
            .Where(incident => intentIds.Contains(incident.Id))
            .OrderBy(incident => incident.CreatedAt)
            .Select(incident => new { incident.Id, incident.SessionId, incident.FailureReason })
            .ToListAsync(ct);
        return rows.Select(row => new RunnerSlotIntentOutcomeDto(
            row.Id,
            row.SessionId ?? Guid.Empty,
            row.FailureReason == ReconciledMarker ? "released"
                : row.FailureReason?.StartsWith(FailedPrefix, StringComparison.Ordinal) == true ? "failed"
                : row.FailureReason?.StartsWith(ClosedPrefix, StringComparison.Ordinal) == true ? "closed"
                : "pending")).ToArray();
    }

    public static async Task<RunnerSlotIntentHistoryDto> GetIntentAsync(
        AppDbContext db, Guid intentId, CancellationToken ct) =>
        await db.AgentIncidents.AsNoTracking()
            .Where(i => i.Id == intentId && i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent)
            .Select(i => new RunnerSlotIntentHistoryDto(i.Id, i.SessionId, i.Message, i.FailureReason, i.CreatedAt))
            .SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Runner slot release intent", intentId);

    internal static IQueryable<AgentIncident> PendingIntents(AppDbContext db) =>
        db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent
                && i.FailureReason != null && i.FailureReason.StartsWith(PendingPrefix));

    internal static async Task<RunnerSlotPendingIntentsPageDto> ListPendingIntentsAsync(
        AppDbContext db, string? runnerId, Guid? sessionId, Guid? after, int limit, CancellationToken ct)
    {
        var query = PendingIntents(db);
        if (runnerId is { Length: > 0 })
        {
            var direct = PendingPrefix + runnerId;
            var orphan = PendingPrefix + OrphanMarker + runnerId;
            var kill = PendingKillGenerationPrefix + runnerId + ":";
            query = query.Where(i => i.FailureReason == direct || i.FailureReason == orphan
                || (i.FailureReason!.StartsWith(kill) && !i.FailureReason.Substring(kill.Length).Contains(":")));
        }
        if (sessionId is Guid session)
            query = query.Where(i => i.SessionId == session);
        if (after is Guid cursor)
            query = query.Where(i => i.Id.CompareTo(cursor) > 0);
        var intents = await query.OrderBy(i => i.Id).Take(limit + 1)
            .Select(i => new RunnerSlotIntentHistoryDto(i.Id, i.SessionId, i.Message, i.FailureReason, i.CreatedAt))
            .ToListAsync(ct);
        if (intents.Count <= limit)
            return new(intents, null);
        intents.RemoveAt(limit);
        return new(intents, intents[^1].IntentId);
    }

    public static async Task<RunnerSlotIntentHistoryDto> CloseIntentAsync(
        AppDbContext db, Guid intentId, string requester, string evidence, TimeProvider clock, CancellationToken ct)
    {
        var intent = await GetIntentAsync(db, intentId, ct);
        if (intent.FailureReason?.StartsWith(PendingPrefix, StringComparison.Ordinal) == true)
        {
            var outcome = $"{ClosedPrefix}{clock.GetUtcNow():O} by {requester}: {evidence}";
            var originalState = $" (was {intent.FailureReason})";
            if (outcome.Length + originalState.Length <= AgentIncident.FailureReasonMaxLength)
                outcome += originalState;
            if (await MarkAsync(db, intentId, outcome, ct) == 1)
                return intent with { FailureReason = outcome };
        }

        throw new ConflictException("The runner slot release intent has already settled.", "runner_slot_intent_settled");
    }

    /// <summary>
    /// CARD-0679 D-7: a failed remote launch whose clean-up kill could not be sent (no eligible
    /// connection) leaves this intent instead of only a log line. The reconcile sends a
    /// generation-conditional kill once the runner resolves again; a session with the same id
    /// under another generation is a replacement and is never killed.
    /// </summary>
    public static async Task<Guid> RecordDeferredKillAsync(
        AppDbContext db, string runnerId, Guid sessionId, DateTime acceptedGeneration, string reason,
        CancellationToken ct)
    {
        var message = string.IsNullOrWhiteSpace(reason) ? "deferred kill after a failed remote launch" : reason.Trim();
        if (message.Length > AgentIncident.MessageMaxLength)
            message = message[..AgentIncident.MessageMaxLength];
        var reasonKey = PendingKillGenerationPrefix + runnerId + ":" + acceptedGeneration.Ticks;
        return await RecordPendingOnceAsync(db, NewIntent(sessionId, message, reasonKey), ct);
    }

    private static bool TryParseKillGeneration(string failureReason, out string runnerId, out DateTime generation)
    {
        runnerId = "";
        generation = default;
        const string prefix = PendingKillGenerationPrefix;
        if (!failureReason.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = failureReason[prefix.Length..];
        var split = rest.LastIndexOf(':');
        if (split <= 0 || !long.TryParse(rest[(split + 1)..], out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return false;
        runnerId = rest[..split];
        generation = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    /// <summary>
    /// CARD-0679 D-7: the kill arm. Resolve throws while the runner is unavailable, which leaves the
    /// intent pending (the caller's per-intent catch). A killed, already-exited or unknown session
    /// finishes the intent with an audit naming the outcome; a generation mismatch fails it.
    /// </summary>
    private static async Task<bool> KillGenerationAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, Guid intentId, string runnerId, Guid sessionId,
        DateTime generation, string? reason, CancellationToken ct)
    {
        var runner = directory.Resolve(runnerId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The scan may have read this intent before an operator closed it. Lock and reread
        // before dispatch, and hold the lock through the bounded runner call and terminal write.
        var current = await db.AgentIncidents
            .FromSqlInterpolated($"SELECT * FROM \"AgentIncidents\" WHERE \"Id\" = {intentId} FOR UPDATE SKIP LOCKED")
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
        if (current is null || current.Kind != AgentIncidentKind.RunnerSlotReleaseIntent
            || current.FailureReason?.StartsWith(PendingPrefix, StringComparison.Ordinal) != true)
            return false;

        var result = await runner.KillGenerationAsync(sessionId, generation, ct);
        if (result.Outcome == KillGenerationOutcomes.Mismatch)
        {
            await MarkAsync(db, intentId,
                FailedPrefix + $"runner holds session {sessionId:D} under another generation; nothing killed", ct);
            await transaction.CommitAsync(ct);
            return false;
        }

        await MarkAsync(db, intentId, ReconciledMarker, ct);
        var message = $"Runner '{runnerId}' session {sessionId:D} generation {generation:O} deferred kill: "
            + $"{result.Outcome} ({reason})";
        if (message.Length > AgentIncident.MessageMaxLength)
            message = message[..AgentIncident.MessageMaxLength];
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = AgentIncidentKind.RunnerSlotForceReleased,
            Severity = AlertSeverity.Warning,
            Message = message,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private const string PendingPrefix = "pending:";

    /// <summary>The <c>FailureReason</c> prefix of an intent the reconcile still owes.</summary>
    internal const string PendingIntentPrefix = PendingPrefix;
    internal const string PendingKillGenerationPrefix = PendingPrefix + "kill-generation:";
    private const string OrphanMarker = "orphan:";
    private const string FailedPrefix = "failed:";
    private const string ClosedPrefix = "closed:";
    private const string ReconciledMarker = "reconciled";

    private static (string RunnerId, bool FromOrphanSweep) ParsePending(string failureReason)
    {
        var rest = failureReason[PendingPrefix.Length..];
        return rest.StartsWith(OrphanMarker, StringComparison.Ordinal)
            ? (rest[OrphanMarker.Length..], true)
            : (rest, false);
    }

    private static async Task ReleaseOneAsync(
        ISessionRunnerDirectory directory, AppDbContext db, string runnerId, Guid sessionId, string reason,
        bool automatic, ICollection<Guid>? intents, CancellationToken ct)
    {
        // If the automatic sweep cannot resolve the runner, no command could have been sent.
        // Leave any earlier lost-response obligation alone and let the next sweep retry.
        var runner = automatic ? directory.Resolve(runnerId) : null;
        var intent = NewIntent(sessionId, reason, PendingPrefix + runnerId);
        Guid intentId;
        if (automatic)
        {
            intentId = await RecordPendingOnceAsync(db, intent, ct);
        }
        else
        {
            db.AgentIncidents.Add(intent);
            await db.SaveChangesAsync(ct);
            intentId = intent.Id;
        }
        intents?.Add(intentId);
        try
        {
            await (runner ?? directory.Resolve(runnerId)).ReleaseSlotAsync(sessionId, reason, ct);
        }
        catch (Exception ex) when (ex is ConflictException or RunnerProblemException)
        {
            // A refusal settles this attempt. A reused intent can still owe an audit for
            // an earlier command whose answer was lost.
            var detail = FailedPrefix + ex.Message;
            if (detail.Length > AgentIncident.FailureReasonMaxLength)
                detail = detail[..AgentIncident.FailureReasonMaxLength];
            if (intentId == intent.Id)
                await MarkAsync(db, intentId, detail, CancellationToken.None);
            throw;
        }

        await FinishAsync(db, runnerId, sessionId, reason, ct);
    }

    /// <summary>
    /// Claim every pending intent for this seat and audit once. The claim is a conditional
    /// update inside the audit's transaction, so a concurrent reconcile cannot audit twice and a
    /// failed audit save leaves the intents pending.
    /// </summary>
    private static async Task<bool> FinishAsync(
        AppDbContext db, string runnerId, Guid sessionId, string reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var direct = PendingPrefix + runnerId;
        var swept = PendingPrefix + OrphanMarker + runnerId;
        var claimed = await db.AgentIncidents
            .Where(incident => incident.Kind == AgentIncidentKind.RunnerSlotReleaseIntent
                && incident.SessionId == sessionId
                && (incident.FailureReason == direct || incident.FailureReason == swept))
            .ExecuteUpdateAsync(set => set.SetProperty(incident => incident.FailureReason, ReconciledMarker), ct);
        if (claimed == 0)
            return false;
        await AuditAsync(db, runnerId, sessionId, reason, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static Task<int> MarkAsync(AppDbContext db, Guid intentId, string failureReason, CancellationToken ct) =>
        db.AgentIncidents
            .Where(incident => incident.Id == intentId && incident.Kind == AgentIncidentKind.RunnerSlotReleaseIntent
                && incident.FailureReason != null
                && incident.FailureReason.StartsWith(PendingPrefix))
            .ExecuteUpdateAsync(set => set.SetProperty(incident => incident.FailureReason, failureReason), ct);

    private static async Task<Guid> RecordPendingOnceAsync(AppDbContext db, AgentIncident intent, CancellationToken ct)
    {
        // Serialize producers of this obligation across sweeps and server instances. Keep the
        // original creation time for attention, and release the lock before any runner call.
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var lockKey = $"runner-slot-release:{intent.SessionId:D}:{intent.FailureReason}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var existing = await db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent && i.SessionId == intent.SessionId
                && i.FailureReason == intent.FailureReason)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
            db.AgentIncidents.Add(intent);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return existing ?? intent.Id;
    }

    private static AgentIncident NewIntent(Guid sessionId, string reason, string pendingReason) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        Kind = AgentIncidentKind.RunnerSlotReleaseIntent,
        Severity = AlertSeverity.Warning,
        Message = reason,
        FailureReason = pendingReason,
        CreatedAt = DateTime.UtcNow,
    };

    internal static string RequireReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ValidationException("reason", "A reason is required.");
        return text;
    }

    private static async Task AuditAsync(
        AppDbContext db, string runnerId, Guid sessionId, string reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null
            && session.Status is SessionStatus.Created or SessionStatus.Starting
                or SessionStatus.Running or SessionStatus.Stopping)
        {
            SessionTermination.Record(session, SessionTerminationSource.OperatorRequest);
            session.Status = SessionStatus.Stopped;
            session.EndedAt = now;
            session.LastSeenAt = now;
            session.FailureReason = reason;
        }

        var key = sessionId.ToString("D");
        var agentId = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId == key)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        var message = $"Runner '{runnerId}' slot {sessionId:D} force-released: {reason}";
        if (message.Length > AgentIncident.MessageMaxLength)
            message = message[..AgentIncident.MessageMaxLength];
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = AgentIncidentKind.RunnerSlotForceReleased,
            Severity = AlertSeverity.Warning,
            Message = message,
            CreatedAt = now,
        });
    }

    private static async Task<Dictionary<Guid, DesktopRow>> LoadDesktopAsync(
        AppDbContext db, Guid[] ids, CancellationToken ct)
    {
        var joined = await SeatDesktopJoin.LoadAsync(db, ids, ct);
        var rows = new Dictionary<Guid, DesktopRow>(ids.Length);
        foreach (var id in ids)
        {
            joined.TryGetValue(id, out var row);
            rows[id] = new DesktopRow(row.Live, row.Status, row.OpenTaskId, row.PooledWarm, row.Park);
        }

        return rows;
    }

    private static RunnerSlotParkDto? ToPark(SeatParkRow? park) =>
        park is null
            ? null
            : new RunnerSlotParkDto(
                park.ParkId,
                park.State.ToString(),
                park.ReasonCode,
                park.ReleaseId,
                park.SyncState.ToString());

    private readonly record struct DesktopRow(
        bool Live, string? Status, Guid? OpenTaskId, bool PooledWarm, SeatParkRow? Park);
}

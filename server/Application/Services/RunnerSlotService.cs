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
    /// An orphan has no live desktop session, or a live one with no open task, unless that
    /// session is a warm pool delegate the process-release invariant is keeping on purpose.
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
                row.OpenTaskId);
        }).ToArray();
        var capacity = directory.DeclaredCapacity(runnerId);
        return new RunnerSlotsDto(runnerId, capacity, slots.Count(s => s.OccupiesCapacity), slots);
    }

    public static async Task<RunnerSlotReleaseDto> ReleaseAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, Guid sessionId, string? reason,
        CancellationToken ct, ICollection<Guid>? intents = null)
    {
        var text = RequireReason(reason);
        await ReleaseOneAsync(directory, db, runnerId, sessionId, text, fromOrphanSweep: false, intents, ct);
        return new RunnerSlotReleaseDto(1, [sessionId]);
    }

    public static async Task<RunnerSlotReleaseDto> ReleaseOrphansAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, string? reason, CancellationToken ct,
        ICollection<Guid>? intents = null)
    {
        var text = RequireReason(reason);
        var listed = await ListAsync(directory, db, runnerId, ct);
        var released = new List<Guid>();
        foreach (var slot in listed.Slots)
        {
            if (!slot.Orphan)
                continue;
            // The list is a snapshot. A task can claim the seat before this loop reaches it.
            var current = await LoadDesktopAsync(db, [slot.SessionId], ct);
            if (!current.TryGetValue(slot.SessionId, out var row)
                || row.Status != slot.DesktopStatus
                || row.OpenTaskId != slot.OpenTaskId
                || !IsOrphan(row.Live, row.OpenTaskId is not null, row.PooledWarm))
                continue;
            await ReleaseOneAsync(directory, db, runnerId, slot.SessionId, text, fromOrphanSweep: true, intents, ct);
            released.Add(slot.SessionId);
        }

        return new RunnerSlotReleaseDto(released.Count, released);
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
        var pending = await db.AgentIncidents.AsNoTracking()
            .Where(incident => incident.Kind == AgentIncidentKind.RunnerSlotReleaseIntent
                && incident.FailureReason != null
                && incident.FailureReason.StartsWith(PendingPrefix))
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
    /// refused or claimed, <c>pending</c> while the outcome is still unknown.
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
                : "pending")).ToArray();
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
        var intent = new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = AgentIncidentKind.RunnerSlotReleaseIntent,
            Severity = AlertSeverity.Warning,
            Message = message,
            FailureReason = PendingPrefix + KillGenerationMarker + runnerId + ":" + acceptedGeneration.Ticks,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentIncidents.Add(intent);
        await db.SaveChangesAsync(ct);
        return intent.Id;
    }

    private static bool TryParseKillGeneration(string failureReason, out string runnerId, out DateTime generation)
    {
        runnerId = "";
        generation = default;
        var prefix = PendingPrefix + KillGenerationMarker;
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
        var result = await directory.Resolve(runnerId).KillGenerationAsync(sessionId, generation, ct);
        if (result.Outcome == KillGenerationOutcomes.Mismatch)
        {
            await MarkAsync(db, intentId,
                FailedPrefix + $"runner holds session {sessionId:D} under another generation; nothing killed", ct);
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.AgentIncidents
            .Where(incident => incident.Id == intentId && incident.FailureReason != null
                && incident.FailureReason.StartsWith(PendingPrefix))
            .ExecuteUpdateAsync(set => set.SetProperty(incident => incident.FailureReason, ReconciledMarker), ct);
        if (claimed == 0)
            return false;
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
    private const string KillGenerationMarker = "kill-generation:";
    private const string OrphanMarker = "orphan:";
    private const string FailedPrefix = "failed:";
    private const string ReconciledMarker = "reconciled";

    private static (string RunnerId, bool FromOrphanSweep) ParsePending(string failureReason)
    {
        var rest = failureReason[PendingPrefix.Length..];
        return rest.StartsWith(OrphanMarker, StringComparison.Ordinal)
            ? (rest[OrphanMarker.Length..], true)
            : (rest, false);
    }

    private static async Task ReleaseOneAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, Guid sessionId, string reason,
        bool fromOrphanSweep, ICollection<Guid>? intents, CancellationToken ct)
    {
        var intent = NewIntent(runnerId, sessionId, reason, fromOrphanSweep);
        db.AgentIncidents.Add(intent);
        await db.SaveChangesAsync(ct);
        intents?.Add(intent.Id);
        try
        {
            await directory.Resolve(runnerId).ReleaseSlotAsync(sessionId, reason, ct);
        }
        catch (Exception ex) when (ex is ConflictException or RunnerProblemException)
        {
            // The runner answered and refused: nothing was released, so the intent is final.
            // A lost answer (transport, timeout, unavailable) stays pending for the reconcile.
            var detail = FailedPrefix + ex.Message;
            if (detail.Length > AgentIncident.FailureReasonMaxLength)
                detail = detail[..AgentIncident.FailureReasonMaxLength];
            await MarkAsync(db, intent.Id, detail, CancellationToken.None);
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
            .Where(incident => incident.Id == intentId && incident.FailureReason != null
                && incident.FailureReason.StartsWith(PendingPrefix))
            .ExecuteUpdateAsync(set => set.SetProperty(incident => incident.FailureReason, failureReason), ct);

    private static AgentIncident NewIntent(string runnerId, Guid sessionId, string reason, bool fromOrphanSweep) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        Kind = AgentIncidentKind.RunnerSlotReleaseIntent,
        Severity = AlertSeverity.Warning,
        Message = reason,
        FailureReason = PendingPrefix + (fromOrphanSweep ? OrphanMarker : "") + runnerId,
        CreatedAt = DateTime.UtcNow,
    };

    private static string RequireReason(string? reason)
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
        var rows = new Dictionary<Guid, DesktopRow>();
        if (ids.Length == 0)
            return rows;
        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Status })
            .ToListAsync(ct);
        var open = await db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId != null
                && ids.Contains(t.AgentSessionId.Value)
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
            .Select(t => new { SessionId = t.AgentSessionId!.Value, t.Id })
            .ToListAsync(ct);
        var keys = ids.Select(id => id.ToString("D")).ToArray();
        var pooled = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null
                && keys.Contains(a.PersistentSessionId)
                && a.Status == AgentStatus.Idle
                && a.PoolIdleSince != null)
            .Select(a => a.PersistentSessionId!)
            .ToListAsync(ct);
        var pooledIds = pooled
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        foreach (var id in ids)
        {
            var session = sessions.FirstOrDefault(s => s.Id == id);
            var live = session is not null
                && session.Status is SessionStatus.Created or SessionStatus.Starting
                    or SessionStatus.Running or SessionStatus.Stopping;
            var taskId = open.FirstOrDefault(t => t.SessionId == id)?.Id;
            rows[id] = new DesktopRow(live, session?.Status.ToString(), taskId, pooledIds.Contains(id));
        }

        return rows;
    }

    private readonly record struct DesktopRow(bool Live, string? Status, Guid? OpenTaskId, bool PooledWarm);
}

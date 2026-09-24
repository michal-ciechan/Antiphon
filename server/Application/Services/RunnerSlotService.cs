using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
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
        CancellationToken ct)
    {
        var text = RequireReason(reason);
        await ReleaseOneAsync(directory, db, runnerId, sessionId, text, ct);
        return new RunnerSlotReleaseDto(1, [sessionId]);
    }

    public static async Task<RunnerSlotReleaseDto> ReleaseOrphansAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, string? reason, CancellationToken ct)
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
            await ReleaseOneAsync(directory, db, runnerId, slot.SessionId, text, ct);
            released.Add(slot.SessionId);
        }

        return new RunnerSlotReleaseDto(released.Count, released);
    }

    /// <summary>
    /// Finish desktop audits whose runner release already happened and whose save did not.
    /// A seat the runner still lists is released again; a seat it has already dropped is only audited.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ReconcilePendingReleasesAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, CancellationToken ct)
    {
        var pending = await db.AgentIncidents
            .Where(incident => incident.Kind == AgentIncidentKind.RunnerSlotReleaseIntent
                && incident.FailureReason != null
                && incident.FailureReason.StartsWith(PendingPrefix))
            .ToListAsync(ct);
        var finished = new List<Guid>();
        foreach (var intent in pending)
        {
            if (intent.SessionId is not Guid sessionId)
                continue;
            var runnerId = intent.FailureReason![PendingPrefix.Length..];
            var listed = await directory.Resolve(runnerId).ListAsync(ct);
            if (listed.Any(session => session.SessionId == sessionId))
                await directory.Resolve(runnerId).ReleaseSlotAsync(sessionId, intent.Message, ct);
            await AuditAsync(db, runnerId, sessionId, intent.Message, ct);
            intent.FailureReason = ReconciledMarker;
            finished.Add(sessionId);
        }

        if (finished.Count > 0)
            await db.SaveChangesAsync(ct);
        return finished;
    }

    private const string PendingPrefix = "pending:";
    private const string ReconciledMarker = "reconciled";

    private static async Task ReleaseOneAsync(
        PhoneHomeRunnerDirectory directory, AppDbContext db, string runnerId, Guid sessionId, string reason,
        CancellationToken ct)
    {
        db.AgentIncidents.Add(NewIntent(runnerId, sessionId, reason));
        await db.SaveChangesAsync(ct);
        await directory.Resolve(runnerId).ReleaseSlotAsync(sessionId, reason, ct);
        await AuditAsync(db, runnerId, sessionId, reason, ct);
        var intent = db.AgentIncidents.Local.First(incident =>
            incident.Kind == AgentIncidentKind.RunnerSlotReleaseIntent
            && incident.SessionId == sessionId
            && incident.FailureReason == PendingPrefix + runnerId);
        intent.FailureReason = ReconciledMarker;
        await db.SaveChangesAsync(ct);
    }

    private static AgentIncident NewIntent(string runnerId, Guid sessionId, string reason) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        Kind = AgentIncidentKind.RunnerSlotReleaseIntent,
        Severity = AlertSeverity.Warning,
        Message = reason,
        FailureReason = PendingPrefix + runnerId,
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

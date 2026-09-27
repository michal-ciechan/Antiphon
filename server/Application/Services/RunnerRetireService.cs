using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable idle and operator retirement of a drained phone-home runner.</summary>
public sealed class RunnerRetireService(
    AppDbContext db,
    ISessionRunnerDirectory directory,
    PhoneHomeRunnerSettings settings,
    TimeProvider clock,
    ILogger logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var rows = await db.SessionRunnerStates
            .Where(row => row.Draining && row.RetireWhenIdle && row.RetiredAt == null)
            .ToListAsync(ct);
        var retired = 0;
        foreach (var row in rows)
            if (await TryRetireIdleAsync(row, ct))
                retired++;
        return retired;
    }

    private async Task<bool> TryRetireIdleAsync(SessionRunnerState row, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (row.DrainedAt is null || now < row.DrainedAt.Value.AddSeconds(settings.RetireMinDrainSeconds))
            return false;

        var bound = await db.AgentSessions.AnyAsync(s => s.RunnerId == row.RunnerId
            && s.Status != SessionStatus.Stopped && s.Status != SessionStatus.Failed, ct);
        var queuedIds = await db.AgentTasks.AsNoTracking()
            .Where(t => t.RunnerId == row.RunnerId && t.Status == AgentTaskStatus.Queued
                && t.AgentSessionId == null)
            .Select(t => t.Id).ToListAsync(ct);
        if (queuedIds.Count > 0)
            logger.LogWarning("Runner {RunnerId} remains draining with queued tasks {TaskIds}",
                row.RunnerId, string.Join(',', queuedIds));
        if (bound || queuedIds.Count > 0)
            return await ClearIdleAsync(row, now, ct);

        var phoneHome = directory as PhoneHomeRunnerDirectory;
        var status = phoneHome?.Status(row.RunnerId);
        if (status is { Available: false })
        {
            // A transport loss alone is not final: the runner may reconnect during its lease.
            var disconnectedAt = status.LastDisconnectAtUtc ?? status.LastHeartbeatUtc ?? row.DrainedAt;
            if (disconnectedAt is not null && now >= disconnectedAt.Value.AddSeconds(settings.LeaseSeconds))
            {
                await StampAsync(row, now, "idle_disconnected", ct);
                return true;
            }
            return false;
        }

        var inventory = await directory.GetInventoryAsync(row.RunnerId, ct);
        if (inventory is not RunnerInventory.Available available
            || available.Sessions.Any(session => session.Status != "Exited"))
            return await ClearIdleAsync(row, now, ct);

        if (row.IdleObservedAt is null)
        {
            row.IdleObservedAt = now;
            row.UpdatedAt = now;
            await SaveAndMirrorAsync(row, ct);
            return false;
        }
        if (now < row.IdleObservedAt.Value.AddSeconds(settings.RetireIdleSeconds))
            return false;

        var live = phoneHome?.SnapshotLive(row.RunnerId);
        if (live is null || !live.SocketOpen)
            return false;
        try
        {
            var result = await new PhoneHomeRunnerClient(live).RetireAsync(false, "idle", ct);
            logger.LogInformation("Runner {RunnerId} accepted idle retirement, boot {ProcessBootId}",
                row.RunnerId, result.ProcessBootId);
        }
        catch (ConflictException ex) when (ex.Code == PhoneHomeProblemTypes.RunnerBusy)
        {
            await ClearIdleAsync(row, now, ct);
            return false;
        }
        await StampAsync(row, clock.GetUtcNow(), "idle", ct);
        return true;
    }

    public async Task ForceAsync(string runnerId, string reason, CancellationToken ct)
    {
        var row = await db.SessionRunnerStates.SingleOrDefaultAsync(s => s.RunnerId == runnerId, ct);
        if (row is not { Draining: true })
            throw new ConflictException($"Runner '{runnerId}' is not draining.",
                PhoneHomeProblemTypes.RunnerNotDraining);
        if (row.RetiredAt is not null)
            return;

        var now = clock.GetUtcNow();
        var sessions = await db.AgentSessions
            .Where(s => s.RunnerId == runnerId
                && s.Status != SessionStatus.Stopped && s.Status != SessionStatus.Failed)
            .ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.Status = SessionStatus.Failed;
            session.FailureReason = $"runner {runnerId} retired by operator: {reason}";
            session.TerminationSource = SessionTerminationSource.SystemRequest;
            session.EndedAt = now.UtcDateTime;
        }
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessions.FirstOrDefault()?.Id,
            Kind = AgentIncidentKind.RunnerForceRetired,
            Severity = AlertSeverity.Warning,
            Message = $"Runner {runnerId} retired by operator: {reason}",
            CreatedAt = now.UtcDateTime,
        });
        await db.SaveChangesAsync(ct);

        if (directory is PhoneHomeRunnerDirectory phoneHome
            && phoneHome.Status(runnerId).Available
            && phoneHome.SnapshotLive(runnerId) is { SocketOpen: true } live)
        {
            try
            {
                await new PhoneHomeRunnerClient(live).RetireAsync(true, reason, ct);
            }
            catch (ServiceUnavailableException ex)
            {
                logger.LogWarning(ex, "Runner {RunnerId} disconnected during forced retirement", runnerId);
            }
        }
        await StampAsync(row, clock.GetUtcNow(), $"forced:{reason}", ct);
    }

    private async Task<bool> ClearIdleAsync(SessionRunnerState row, DateTimeOffset now, CancellationToken ct)
    {
        if (row.IdleObservedAt is null)
            return false;
        row.IdleObservedAt = null;
        row.UpdatedAt = now;
        await SaveAndMirrorAsync(row, ct);
        return false;
    }

    private async Task StampAsync(SessionRunnerState row, DateTimeOffset now, string reason, CancellationToken ct)
    {
        row.RetiredAt = now;
        row.RetireReason = reason;
        row.UpdatedAt = now;
        await SaveAndMirrorAsync(row, ct);
    }

    private async Task SaveAndMirrorAsync(SessionRunnerState row, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (directory is PhoneHomeRunnerDirectory phoneHome)
            phoneHome.ApplyState(row.RunnerId, RunnerStateService.ToState(row));
    }
}

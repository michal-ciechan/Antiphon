using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1079: one occupancy sample per host. Reads runner inventory and desktop rows, writes
/// <see cref="HostOccupancySample"/>, and publishes a snapshot. It never stops, kills, releases
/// or writes a session or task.
/// </summary>
public sealed class SeatOccupancySampler(
    AppDbContext db,
    ISessionRunnerDirectory directory,
    HostBudgetService budgets,
    SeatOccupancyState state,
    IOptions<AttentionSettings> attention,
    TimeProvider clock,
    ILogger<SeatOccupancySampler> logger,
    RemoteWorkspacePreparer? preparer = null)
{
    private const int LoggedDetailMaxLength = 2000;

    public async Task SampleOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var settings = attention.Value;
        var limits = await budgets.ListAsync(ct);
        var hosts = new List<HostOccupancyObservation>(limits.Count);
        foreach (var limit in limits)
        {
            var observation = limit.HostId == "local"
                ? await SampleLocalAsync(limit, ct)
                : await SampleRunnerAsync(limit, settings, ct);
            hosts.Add(observation);
        }

        var cutoff = now.AddDays(-settings.OccupancySampleRetentionDays);
        await db.HostOccupancySamples.Where(row => row.SampledAt < cutoff).ExecuteDeleteAsync(ct);
        foreach (var observation in hosts)
            db.HostOccupancySamples.Add(ToRow(observation, now));
        await db.SaveChangesAsync(ct);
        state.Publish(new SeatOccupancySnapshot(now, hosts));
    }

    private async Task<HostOccupancyObservation> SampleLocalAsync(HostLimit limit, CancellationToken ct)
    {
        var active = await db.AgentTasks.AsNoTracking().Where(AgentTaskRoles.NotSpecialist)
            .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && !t.CapacityWaitRetained
                && (t.RunnerId == null || t.RunnerId == ""), ct);
        return new HostOccupancyObservation(
            limit.HostId, "local", "local", null,
            active, active, 0, 0, 0, 0, 0, 0,
            limit.Effective, limit.Declared, null, []);
    }

    private async Task<HostOccupancyObservation> SampleRunnerAsync(
        HostLimit limit, AttentionSettings settings, CancellationToken ct)
    {
        var hostId = limit.HostId;
        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => s.RunnerId == hostId
                && (s.Status == SessionStatus.Created || s.Status == SessionStatus.Starting
                    || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping))
            .Select(s => new DesktopSession(s.Id, s.StartedAt))
            .ToListAsync(ct);
        var pending = await db.AgentTasks.AsNoTracking().CountAsync(t => t.RunnerId == hostId
            && t.Status == AgentTaskStatus.Queued && t.AgentSessionId == null
            && t.RemoteWorktreePath != null, ct);
        var dispatchedWorking = await db.AgentTasks.AsNoTracking().CountAsync(t => t.RunnerId == hostId
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct);
        var mirrorCount = preparer?.InFlightCount(hostId, Guid.Empty) ?? 0;
        var inventory = await ReadInventoryAsync(hostId, settings, ct);

        var ids = sessions.Select(s => s.Id).ToHashSet();
        IReadOnlyList<SessionRunnerSessionDto> listed = [];
        string inventoryState;
        string? inventoryReason;
        if (inventory is RunnerInventory.Available available)
        {
            listed = available.Sessions;
            foreach (var session in listed)
                ids.Add(session.SessionId);
            inventoryState = "listed";
            inventoryReason = null;
        }
        else
        {
            var unavailable = (RunnerInventory.Unavailable)inventory;
            inventoryState = "unavailable";
            inventoryReason = RenderInventoryReason(hostId, unavailable.Reason);
        }

        var desktop = await SeatDesktopJoin.LoadAsync(db, ids.ToArray(), ct);
        var runnerStarted = listed.ToDictionary(s => s.SessionId, s => s.StartedAt);
        var idleSince = new List<DateTime>();
        var pooled = 0;
        foreach (var session in sessions)
        {
            desktop.TryGetValue(session.Id, out var row);
            if (row.PooledWarm)
                pooled++;
            if (!IsDesktopIdle(row))
                continue;
            var started = runnerStarted.TryGetValue(session.Id, out var at) ? at : session.StartedAt;
            idleSince.Add(IdleSince(row, started));
        }

        var seats = new List<SeatObservation>(listed.Count);
        var orphans = 0;
        foreach (var session in listed)
        {
            desktop.TryGetValue(session.SessionId, out var row);
            var occupies = RunnerSlotService.OccupiesCapacity(session.Status);
            var orphan = RunnerSlotService.IsOrphan(row.Live, row.OpenTaskId is not null, row.PooledWarm);
            if (occupies && orphan)
                orphans++;
            var seatClass = SeatOccupancyProjection.Classify(
                occupies, row.Live, row.PooledWarm, row.LatestTask?.Status);
            seats.Add(new SeatObservation(
                hostId,
                session.SessionId,
                session.Status,
                session.Pid,
                session.StartedAt,
                occupies,
                orphan,
                row.PooledWarm,
                row.Status,
                row.LatestTask?.Id,
                row.LatestTask?.Status,
                row.LatestTask?.Attempt,
                row.LatestTask?.Role,
                row.LatestTask?.CardId,
                row.LatestTask?.BoardId,
                row.LatestTask?.AgentId,
                seatClass,
                SeatOccupancyProjection.IdleSince(
                    seatClass, row.BlockedAt, row.LatestTask?.CompletedAt, session.StartedAt),
                SeatOccupancyProjection.Pushed(row.PublicationReceipt)));
        }

        return new HostOccupancyObservation(
            hostId, "runner", inventoryState, inventoryReason,
            sessions.Count + pending + mirrorCount, dispatchedWorking,
            sessions.Count, pending, mirrorCount, idleSince.Count, pooled, orphans,
            limit.Effective, limit.Declared,
            idleSince.Count == 0 ? null : idleSince.Min(),
            seats);
    }

    private async Task<RunnerInventory> ReadInventoryAsync(
        string hostId, AttentionSettings settings, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(settings.InventoryTimeoutMs), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            return await directory.GetInventoryAsync(hostId, linked.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Seat occupancy inventory timed out for {HostId}", hostId);
            return new RunnerInventory.Unavailable("timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Seat occupancy inventory failed for {HostId}", hostId);
            return new RunnerInventory.Unavailable("error");
        }
    }

    private static bool IsDesktopIdle(SeatDesktopRow row) =>
        row.Live && !row.PooledWarm
        && row.LatestTask?.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working);

    private static DateTime IdleSince(SeatDesktopRow row, DateTime started)
    {
        var seatClass = SeatOccupancyProjection.Classify(true, row.Live, row.PooledWarm, row.LatestTask?.Status);
        return SeatOccupancyProjection.IdleSince(seatClass, row.BlockedAt, row.LatestTask?.CompletedAt, started);
    }

    /// <summary>
    /// Audit category only. Raw exception text stays in the log and never in the stored reason.
    /// </summary>
    private string RenderInventoryReason(string hostId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "unavailable";

        var trimmed = reason.Trim();
        var rendered = trimmed switch
        {
            "timeout" or "inventory timed out" => "timeout",
            "unavailable" => "unavailable",
            "phone-home runner unavailable" => "unavailable: phone-home",
            "error" => "error",
            _ => null,
        };
        if (rendered is not null)
            return rendered;

        var detail = trimmed.Length <= LoggedDetailMaxLength ? trimmed : trimmed[..LoggedDetailMaxLength];
        logger.LogWarning(
            "Seat occupancy inventory failed for {HostId}; audit category is error. Detail: {Detail}",
            hostId, detail);
        return "error";
    }

    private static HostOccupancySample ToRow(HostOccupancyObservation observation, DateTime sampledAt) => new()
    {
        Id = Guid.NewGuid(),
        HostId = observation.HostId,
        SampledAt = sampledAt,
        InventoryState = observation.InventoryState,
        InventoryReason = observation.InventoryReason,
        InFlight = observation.InFlight,
        DispatchedWorking = observation.DispatchedWorking,
        Sessions = observation.Sessions,
        PendingLaunch = observation.PendingLaunch,
        InFlightMirrors = observation.InFlightMirrors,
        IdleSeats = observation.IdleSeats,
        PooledWarmSeats = observation.PooledWarmSeats,
        OrphanSlots = observation.OrphanSlots,
        EffectiveLimit = observation.EffectiveLimit,
        DeclaredCapacity = observation.DeclaredCapacity,
        OldestIdleSince = observation.OldestIdleSince,
    };

    private readonly record struct DesktopSession(Guid Id, DateTime StartedAt);
}

using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One predicate for the CARD-0079 seat. Admission stays closed from confirmation
/// until the resumed generation is waiting for a new Check. AwaitingCheck admits
/// only that new Check. Automatic restart stays down for every unresolved episode.
/// </summary>
public static class CheckCompactionAdmission
{
    public static bool ClosesAdmission(CheckCompactionRecoveryState state) =>
        state is CheckCompactionRecoveryState.Confirmed
            or CheckCompactionRecoveryState.StopRequested
            or CheckCompactionRecoveryState.Stopped
            or CheckCompactionRecoveryState.ResumeReserved
            or CheckCompactionRecoveryState.NeedsDecision
            or CheckCompactionRecoveryState.DisabledNeedsDecision;

    public static async Task<bool> ClosesSeatAsync(AppDbContext db, Guid physicalAgentId, CancellationToken ct)
    {
        var state = await ActiveStateAsync(db, physicalAgentId, ct);
        return state is { } value && ClosesAdmission(value);
    }

    public static async Task<bool> BlocksAutomaticRestartAsync(
        AppDbContext db, Guid agentId, CancellationToken ct)
    {
        var state = await ActiveStateAsync(db, agentId, ct);
        return state is { } value && CheckCompactionRecoveryStates.IsUnresolved(value);
    }

    public static async Task<HashSet<Guid>> ClosedSeatIdsAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.AgentSupervisionStates.AsNoTracking()
            .Where(s => s.ActiveCompactionRecoveryId != null)
            .Join(
                db.CheckCompactionRecoveries.AsNoTracking(),
                s => s.ActiveCompactionRecoveryId,
                r => r.Id,
                (s, r) => new { s.AgentId, r.State })
            .ToListAsync(ct);
        var closed = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (ClosesAdmission(row.State))
                closed.Add(row.AgentId);
        }

        return closed;
    }

    /// <summary>
    /// Generic interrupted-launch adoption may finish the reserved G2 only.
    /// Any other unresolved episode must not gain a process from that path.
    /// </summary>
    public static async Task<bool> BlocksGenericLaunchResumeAsync(
        AppDbContext db, Guid sessionId, DateTime startedAt, CancellationToken ct)
    {
        var episode = await db.CheckCompactionRecoveries.AsNoTracking()
            .Where(r => r.SessionId == sessionId || r.ResumeSessionId == sessionId)
            .Where(r => db.AgentSupervisionStates.Any(s => s.ActiveCompactionRecoveryId == r.Id))
            .Select(r => new { r.State, r.ResumeAcceptedStartedAt })
            .FirstOrDefaultAsync(ct);
        if (episode is null || !CheckCompactionRecoveryStates.IsUnresolved(episode.State))
            return false;
        return episode.State != CheckCompactionRecoveryState.ResumeReserved
            || episode.ResumeAcceptedStartedAt is null
            || !SessionGeneration.Equal(episode.ResumeAcceptedStartedAt, startedAt);
    }

    private static async Task<CheckCompactionRecoveryState?> ActiveStateAsync(
        AppDbContext db, Guid agentId, CancellationToken ct)
    {
        var active = await db.AgentSupervisionStates.AsNoTracking()
            .Where(s => s.AgentId == agentId)
            .Select(s => s.ActiveCompactionRecoveryId)
            .FirstOrDefaultAsync(ct);
        if (active is null)
            return null;
        return await db.CheckCompactionRecoveries.AsNoTracking()
            .Where(r => r.Id == active.Value)
            .Select(r => (CheckCompactionRecoveryState?)r.State)
            .FirstOrDefaultAsync(ct);
    }
}

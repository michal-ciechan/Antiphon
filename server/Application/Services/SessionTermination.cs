using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// First-writer-wins stamp for <see cref="AgentSession.TerminationSource"/> (CARD-0256, CARD-0316).
/// Platform closers record a source with the terminal status write; an exit event never erases a
/// request, and a request never writes <see cref="SessionTerminationSource.Unknown"/>.
/// ProcessExit records an observation, never voluntary exit or a signal sender.
/// </summary>
public static class SessionTermination
{
    /// <summary>First writer wins (CARD-0256). Returns true when it wrote.</summary>
    public static bool Record(AgentSession session, SessionTerminationSource source)
    {
        if (source == SessionTerminationSource.Unknown
            || session.TerminationSource != SessionTerminationSource.Unknown)
            return false;
        session.TerminationSource = source;
        return true;
    }

    internal static async Task<bool> RecordObservedPoolStopAsync(
        AppDbContext db, AgentSession session, AgentExitReason reason, CancellationToken ct)
    {
        // The Windows runner reports an actual exit code and an observed absence of POSIX
        // signal. Scope its requested cause to an idle pool seat, as used by the reaper fallback.
        if (reason != AgentExitReason.KilledByRequest || session.ExitSignal != 0 || session.ExitCode is null
            || session.Status is not (SessionStatus.Starting or SessionStatus.Running or SessionStatus.Stopping)
            || session.StandingAgentId is not null || session.CardId is not null)
            return false;

        var sessionKey = session.Id.ToString("D");
        var owners = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && a.PersistentSessionId.ToLower() == sessionKey)
            .Select(a => a.Id).Take(2).ToListAsync(ct);
        if (owners.Count > 1) return false;
        var ownerId = owners.Count == 1 ? owners[0]
            : await db.AgentTasks.AsNoTracking().Where(t => t.AgentSessionId == session.Id)
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Select(t => t.AgentId).FirstOrDefaultAsync(ct);
        if (ownerId is not Guid owner || !await db.Agents.AsNoTracking().AnyAsync(
            a => a.Id == owner && a.IsPoolDelegate && !a.AlwaysOn && a.CurrentCardId == null, ct))
            return false;
        if (await db.AgentTasks.AsNoTracking().AnyAsync(t => (t.AgentId == owner || t.AgentSessionId == session.Id)
            && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked), ct))
            return false;

        return Record(session, SessionTerminationSource.SystemRequest) || SessionExitPolicy.IsRequested(session.TerminationSource);
    }

    /// <summary>
    /// Preserve legacy/no-signal source behavior. A known signal plus a cause token alone
    /// cannot manufacture an independently stored platform request; require live issued evidence.
    /// </summary>
    public static SessionTerminationSource FromExitReason(AgentExitReason reason, int? signal = null,
        ProcessStopIntentDto? intent = null) =>
        reason is AgentExitReason.CpuSpinKilled or AgentExitReason.MemoryKilled
            && (signal is null or 0 || intent is { Provenance: ProcessStopProvenance.LiveIssuer,
                Outcome: ProcessStopOutcome.Issued, Target: ProcessStopTarget.Child or ProcessStopTarget.Custody }
                && intent.AttemptedSignal == signal)
            ? SessionTerminationSource.SystemRequest
            : SessionTerminationSource.ProcessExit;
}

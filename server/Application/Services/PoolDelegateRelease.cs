using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0691: the one pool-delegate release rule, shared by settlement
/// (<c>AgentTaskReplyService.ReleaseDelegateAsync</c>), the dispatcher's pool-release sweep and the
/// warm-agent janitor so none of them can drift from the others.
///
/// <para>A Shared delegate whose session is live goes warm; anything else is killed, and the agent
/// row — the process's only owner — goes only once the session is verified terminal (D-3). A kill
/// that did not take leaves the row Idle for the janitor instead of deleting it while the process
/// lives on, which is the CARD-0221 zombie by construction.</para>
/// </summary>
internal static class PoolDelegateRelease
{
    internal enum KillOutcome
    {
        /// <summary>The session row is Stopped/Failed (or absent): the agent row may go.</summary>
        SessionTerminal,

        /// <summary>The kill threw or did not end the session: keep the row.</summary>
        SessionStillLive,
    }

    /// <summary>Warm pool state, reserved for the run that just used it (settlement's rule).</summary>
    internal static void PoolWarm(Agent agent, Guid reservedForRootTaskId, DateTime now)
    {
        agent.Status = AgentStatus.Idle;
        agent.PoolIdleSince = now;
        agent.PoolReservedForRootTaskId = reservedForRootTaskId;
        agent.UpdatedAt = now;
    }

    /// <summary>Idle for the janitor, and not claimable by a caller's follow-up (no reservation).</summary>
    internal static void MarkIdleForJanitor(Agent agent, DateTime now)
    {
        agent.Status = AgentStatus.Idle;
        agent.PoolIdleSince = now;
        agent.PoolReservedForRootTaskId = null;
        agent.UpdatedAt = now;
    }

    /// <summary>
    /// True when nothing is known to still be running for <paramref name="sessionId"/>: no id, no
    /// row, or a row that is Stopped/Failed. Created/Starting/Running/Stopping are not terminal —
    /// Stopping is exactly the row whose kill threw. A fresh no-tracking read, so a kill that
    /// committed through its own isolated context is seen.
    /// </summary>
    internal static async Task<bool> IsSessionTerminalAsync(AppDbContext db, Guid? sessionId, CancellationToken ct)
    {
        if (sessionId is not Guid id)
            return true;
        var status = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => (SessionStatus?)s.Status)
            .FirstOrDefaultAsync(ct);
        return status is null or SessionStatus.Stopped or SessionStatus.Failed;
    }

    /// <summary>
    /// Kill the delegate's session through <paramref name="kill"/> (a failure is logged, never
    /// thrown) and report whether the session is now terminal. The caller removes or keeps the row.
    /// </summary>
    internal static async Task<KillOutcome> KillAndVerifyAsync(
        AppDbContext db, Agent agent, Guid? sessionId, Func<Guid, CancellationToken, Task> kill,
        ILogger logger, CancellationToken ct)
    {
        if (sessionId is Guid sid)
        {
            try
            {
                await kill(sid, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not stop pool delegate '{Name}' session {SessionId}", agent.Name, sid);
            }
        }

        return await IsSessionTerminalAsync(db, sessionId, ct)
            ? KillOutcome.SessionTerminal
            : KillOutcome.SessionStillLive;
    }

    /// <summary>
    /// One <see cref="AgentIncidentKind.DelegateReleaseUnresolved"/> per (agent, session), added to
    /// <paramref name="db"/> without saving. Checks this tracker too, so two release passes in one
    /// unit of work cannot write it twice.
    /// </summary>
    internal static async Task RecordUnresolvedOnceAsync(
        AppDbContext db, Guid agentId, Guid? sessionId, string message, DateTime now, CancellationToken ct)
    {
        var kind = AgentIncidentKind.DelegateReleaseUnresolved;
        if (db.AgentIncidents.Local.Any(i => i.AgentId == agentId && i.SessionId == sessionId && i.Kind == kind)
            || await db.AgentIncidents.AnyAsync(
                i => i.AgentId == agentId && i.SessionId == sessionId && i.Kind == kind, ct))
            return;

        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = kind,
            Severity = AlertSeverity.Error,
            Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
            CreatedAt = now,
        });
    }
}

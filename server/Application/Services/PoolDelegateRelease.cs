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
        /// <summary>The session is terminal and no live or unknown runtime owner remains.</summary>
        SessionTerminal,

        /// <summary>The kill threw or did not end the session: keep the row.</summary>
        SessionStillLive,
    }

    internal static bool CanRelease(Agent agent) => agent.IsPoolDelegate && !agent.AlwaysOn
        && agent.BoardId is null && agent.StandingSpecialistRole is null && agent.StandingSpecialistOwnerId is null;

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
    /// A terminal row is necessary, but a live or unknown runtime session vetoes removal. Failed
    /// can describe a launch/kill failure while the process still lives (CARD-0056/0679). A missing
    /// row is not exit evidence. Read fresh so isolated kills and exit observers are visible.
    /// Without runtime evidence, only a locally recorded Stopped row is accepted.
    /// </summary>
    internal static async Task<bool> IsSessionTerminalAsync(
        AppDbContext db, Guid? sessionId, AgentSessionRuntime? runtime, CancellationToken ct)
    {
        if (sessionId is not Guid id)
            return true;
        var session = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new { s.Status, s.RunnerId })
            .FirstOrDefaultAsync(ct);
        if (session is null || session.Status is not (SessionStatus.Stopped or SessionStatus.Failed))
            return false;
        return runtime is not null
            ? !runtime.IsLiveOrUnknown(id, session.RunnerId)
            : session.RunnerId is null && session.Status == SessionStatus.Stopped;
    }

    /// <summary>
    /// Kill the delegate's session through <paramref name="kill"/> (a failure is logged, never
    /// thrown) and report whether the session is now terminal. The caller removes or keeps the row.
    /// </summary>
    internal static async Task<KillOutcome> KillAndVerifyAsync(
        AppDbContext db, Agent agent, Guid? sessionId, Func<Guid, CancellationToken, Task> kill,
        ILogger logger, AgentSessionRuntime? runtime, CancellationToken ct)
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

        return await IsSessionTerminalAsync(db, sessionId, runtime, ct)
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

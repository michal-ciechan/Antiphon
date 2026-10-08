using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1151 (Q-1 option B): the overdue sweep's boot-stall branch. A transcript-confirmed boot
/// prompt with no model reply is detection only, whatever the session's Working verdict, runner
/// listing or task status says. Nothing here fails, requeues, stops, releases, holds an alias,
/// writes an incident, sends input or touches a queue row. A terminal session row belongs to
/// the dead-session reconciler (A-1) and a Pending brief to the delivery watchdog (A-2): both
/// stay DetectOnly without an event.
/// </summary>
public sealed partial class AgentTaskDispatcher
{
    /// <summary>
    /// The two ownership facts the emission whitelist reads, in one statement. Null fields are
    /// unknown and never admit an event.
    /// </summary>
    private sealed record BootStallOwnership(bool? SessionTerminal, bool? BriefPending);

    private async Task<BootStallOwnership> BootStallReadOwnershipAsync(
        AgentTask task, Guid sessionId, CancellationToken ct)
    {
        // Same brief-row predicate as the delivery watchdog (CARD-0117 D7): this task's own first
        // marked delegation row on this session since dispatch.
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var dispatched = task.DispatchedAt;
        var row = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new
            {
                s.Status,
                s.EndedAt,
                Brief = _db.SessionQueuedMessages
                    .Where(m => m.AgentSessionId == sessionId
                        && m.Origin == QueuedMessageOrigin.Delegation
                        && (dispatched == null || m.CreatedAt >= dispatched)
                        && m.Body.Contains(marker))
                    .OrderBy(m => m.Sequence)
                    .Select(m => (QueuedMessageStatus?)m.Status)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return new(SessionTerminal: null, BriefPending: null);

        var terminal = AgentTaskLiveness.IsDeadSession(
            sessionId, new AgentTaskLiveness.SessionSnapshot(row.Status, row.EndedAt, null));
        return new(terminal, row.Brief == QueuedMessageStatus.Pending);
    }

    private BootStallPolicy.Decision BootStallDecide(
        AgentTask task, Guid sessionId, BootStallFacts boot, BootStallOwnership ownership) =>
        BootStallPolicy.Decide(new BootStallPolicy.Observation(
            boot,
            ownership.SessionTerminal,
            ownership.BriefPending,
            // Gates 1 and 1b returned before any boot work; reaching here is the positive answer.
            ApiRecoveryUnresolved: false,
            CommitRecoveryPending: false,
            IdentityMatches: task.AgentSessionId == sessionId
                && task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working,
            UtcNow()));

    /// <summary>
    /// First-pass decision on the stored rows. False returns DetectOnly before the runner pull:
    /// nothing is due, or the reconciler/watchdog owns the task.
    /// </summary>
    private Task<bool> BootStallNeedsPullAsync(
        AgentTask task, Guid sessionId, BootStallFacts boot, BootStallOwnership ownership, CancellationToken ct)
    {
        var decision = BootStallDecide(task, sessionId, boot, ownership);
        if (decision.Stage == BootStallPolicy.Stage.None)
        {
            _logger.LogDebug(
                "Task {ShortId}: unresolved boot prompt on session {SessionId}, detection only ({Reason})",
                DelegationReportFormatter.Short(task.Id), sessionId, decision.Reason);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>Post-pull decision. Detection only; the task's outcome never changes here.</summary>
    private Task BootStallDetectAsync(
        AgentTask task, Guid sessionId, BootStallFacts boot, BootStallOwnership ownership, CancellationToken ct)
    {
        var decision = BootStallDecide(task, sessionId, boot, ownership);
        if (decision.Stage != BootStallPolicy.Stage.None)
        {
            _logger.LogWarning(
                "Task {ShortId}: {Token} on session {SessionId}; detection only, the session is not stopped",
                DelegationReportFormatter.Short(task.Id), decision.Reason, sessionId);
        }

        return Task.CompletedTask;
    }
}

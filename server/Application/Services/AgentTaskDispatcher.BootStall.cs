using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
    /// CARD-1151 test seam: the context the warning writer opens per write. Production leaves it
    /// null and builds a fresh context over this dispatcher's own options, so every interceptor
    /// registered on them sees the telemetry statements too.
    /// </summary>
    internal Func<AppDbContext>? BootStallContextFactory { get; set; }

    private BootStallWarningWriter? _bootStallWarnings;

    private BootStallWarningWriter BootStallWarnings => _bootStallWarnings ??= new(
        BootStallContextFactory ?? (() => new AppDbContext(
            (DbContextOptions<AppDbContext>)_db.GetService<IDbContextOptions>())),
        _eventBus, _logger);

    private static BootStallWarningWriter.Episode BootStallEpisode(AgentTask task, Guid sessionId, BootStallFacts boot) =>
        new(task.Id, task.RootTaskId, task.Attempt, sessionId, task.DispatchedAt, boot,
            BootStallPolicy.EpisodeKey(task.Id, task.Attempt, sessionId, boot));

    /// <summary>
    /// First-pass decision on the stored rows. False returns DetectOnly before the runner pull:
    /// nothing is due, the reconciler/watchdog owns the task, or (A-7) the due stage is already
    /// recorded for this episode, so a detected task costs no runner call until its next stage.
    /// </summary>
    private async Task<bool> BootStallNeedsPullAsync(
        AgentTask task, Guid sessionId, BootStallFacts boot, BootStallOwnership ownership, CancellationToken ct)
    {
        var decision = BootStallDecide(task, sessionId, boot, ownership);
        if (decision.Stage == BootStallPolicy.Stage.None)
        {
            _logger.LogDebug(
                "Task {ShortId}: unresolved boot prompt on session {SessionId}, detection only ({Reason})",
                DelegationReportFormatter.Short(task.Id), sessionId, decision.Reason);
            return false;
        }

        return !await BootStallWarnings.IsRecordedAsync(BootStallEpisode(task, sessionId, boot), decision.Stage, ct);
    }

    /// <summary>
    /// Post-pull decision. Records the due stage once; the task's outcome never changes here,
    /// and nothing the writer reports can change it.
    /// </summary>
    private async Task BootStallDetectAsync(
        AgentTask task, Guid sessionId, BootStallFacts boot, BootStallOwnership ownership, CancellationToken ct)
    {
        var decision = BootStallDecide(task, sessionId, boot, ownership);
        if (decision.Stage == BootStallPolicy.Stage.None)
            return;

        var episode = BootStallEpisode(task, sessionId, boot);
        await BootStallWarnings.RecordAsync(
            episode, decision.Stage, BootStallPolicy.Detail(decision.Stage, episode.Key, boot), UtcNow(), ct);
    }
}

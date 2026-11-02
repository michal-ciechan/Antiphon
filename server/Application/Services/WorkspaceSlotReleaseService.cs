using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0824 S1 D-B/D-10. The server owns the release decision for a runner workspace slot. A slot is
/// released only when its task is terminal, its runner session holds no seat, no settlement sync debt is
/// Pending or Held, and no live task follows up on it. The path is cleared only after the runner's reply
/// (released, already free or quarantined); the slot id and generation stay on the task as history.
/// A refused or lost reply leaves the task owed for the next pass. Blocked tasks are never released.
/// A runner that does not know the slot (<c>slot_unknown</c>) is asked at most <see cref="UnknownSlotAttempts"/>
/// times per slot and generation; then the reconciler stops and records <see cref="WorkspaceSlotCodes.ReleaseStuck"/>
/// for an operator (D-3, review 3a354688), never a silent forever-retry and never a guessed release stamp.
/// The drain rebind shares the count, and the operator records the release through
/// <see cref="RecordOperatorReleaseAsync"/> (review f4c293bc D-1). The count and the stuck record are keyed by the
/// task attempt as well (CARD-1649 A-1): a reset runner can lease the same slot and generation to the same task again.
/// </summary>
public sealed class WorkspaceSlotReleaseService(
    AppDbContext db, RemoteWorkspaceService remote, TimeProvider clock, ILogger<WorkspaceSlotReleaseService> logger)
{
    private static readonly AgentTaskStatus[] Terminal = [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];
    internal const int UnknownSlotAttempts = 3;
    /// <summary>CARD-1649 A-2. The longest requester the operator route records.</summary>
    internal const int MaxRequesterLength = 64;
    internal const string AttemptMismatchCode = "workspace_slot_release_attempt_mismatch";
    internal const string TaskLiveCode = "workspace_slot_release_task_live";
    /// <summary>CARD-1649 A-1. A task in one of these states holds its lease; its release is never recorded.</summary>
    private static readonly AgentTaskStatus[] HoldingLease = [AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];
    private static readonly AgentTaskStatus[] Live =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    /// <summary>One pass. Returns how many slots the runners confirmed released.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var owed = await db.AgentTasks
            .Where(t => t.RemoteWorkspaceSlotId != null && t.RemoteWorkspaceSlotReleasedAt == null && t.RunnerId != null)
            .OrderBy(t => t.CompletedAt)
            .Take(50)
            .ToListAsync(ct);
        var released = 0;
        foreach (var task in owed)
        {
            if (!await EligibleAsync(task, ct))
                continue;
            var unknown = await UnknownAttemptsAsync(db, task, ct);
            if (unknown >= UnknownSlotAttempts)
                continue;
            PhoneHomeWorkspaceSlotReleaseResponse response;
            try
            {
                response = await remote.ReleaseSlotAsync(task, ct);
            }
            catch (ConflictException ex) when (IsUnknownSlot(ex))
            {
                RecordUnknown(db, task, unknown, clock.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Workspace slot {Slot} generation {Generation} of task {Task} is unknown to runner {Runner} (attempt {Attempt})",
                    task.RemoteWorkspaceSlotId, task.RemoteWorkspaceSlotGeneration, task.Id, task.RunnerId, unknown + 1);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Workspace slot {Slot} generation {Generation} of task {Task} was not released: {Reason}",
                    task.RemoteWorkspaceSlotId, task.RemoteWorkspaceSlotGeneration, task.Id, ex.Message);
                continue;
            }

            if (response.Outcome is not ("released" or "already_free" or "quarantined"))
                continue;
            var now = clock.GetUtcNow().UtcDateTime;
            task.RemoteWorktreePath = null;
            task.RemoteWorkspaceSlotReleasedAt = now;
            if (response.Outcome == "quarantined")
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = task.Id,
                    Type = AgentTaskEventType.Warning,
                    Detail = $"{WorkspaceSlotCodes.Quarantined}: runner '{task.RunnerId}' kept slot {task.RemoteWorkspaceSlotId}'s directory in quarantine (unpushed work, dirt or a lingering process) and re-homed the slot.",
                    At = now,
                });
            await db.SaveChangesAsync(ct);
            released++;
        }

        return released;
    }

    private static string SlotText(AgentTask task) =>
        $"slot {task.RemoteWorkspaceSlotId} generation {task.RemoteWorkspaceSlotGeneration ?? 0}";

    /// <summary>CARD-1649 A-1. The slot, generation and task attempt a stuck release belongs to.</summary>
    private static string AttemptText(AgentTask task) => SlotText(task) + $" task attempt {task.Attempt}";

    private static string UnknownPrefix(AgentTask task) => WorkspaceSlotCodes.ReleaseUnknown + ": " + AttemptText(task) + " ";

    private static string StuckPrefix(AgentTask task) => WorkspaceSlotCodes.ReleaseStuck + ": " + AttemptText(task) + " ";

    /// <summary>A 409 whose runner detail is <see cref="WorkspaceSlotCodes.UnknownSlot"/>: the runner has no record of the slot.</summary>
    internal static bool IsUnknownSlot(Exception ex) =>
        ex is ConflictException && ex.Message.StartsWith(WorkspaceSlotCodes.UnknownSlot, StringComparison.Ordinal);

    /// <summary>
    /// How many times the task's runner has answered <see cref="WorkspaceSlotCodes.UnknownSlot"/> for this slot,
    /// generation and task attempt. The terminal reconciler and the drain rebind (review f4c293bc D-1) share the one count.
    /// </summary>
    internal static Task<int> UnknownAttemptsAsync(AppDbContext db, AgentTask task, CancellationToken ct)
    {
        var prefix = UnknownPrefix(task);
        return db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail != null && e.Detail.StartsWith(prefix), ct);
    }

    /// <summary>
    /// Adds the attempt record after a <see cref="WorkspaceSlotCodes.UnknownSlot"/> answer and, on the last attempt,
    /// the <see cref="WorkspaceSlotCodes.ReleaseStuck"/> record. The caller saves. Returns true once the bound is reached.
    /// </summary>
    internal static bool RecordUnknown(AppDbContext db, AgentTask task, int prior, DateTime at)
    {
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Warning,
            Detail = UnknownPrefix(task) + $"was refused as {WorkspaceSlotCodes.UnknownSlot} by runner '{task.RunnerId}' (attempt {prior + 1} of {UnknownSlotAttempts}).",
            At = at,
        });
        if (prior + 1 < UnknownSlotAttempts)
            return false;
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Warning,
            Detail = StuckPrefix(task) + $"is unknown to runner '{task.RunnerId}' after {UnknownSlotAttempts} attempts; nothing asks it again. "
                + "Find the runner that holds the lease and release it there, or record the release with "
                + $"POST {RecordReleaseRoute(task.RunnerId!, task.RemoteWorkspaceSlotId!)}.",
            At = at,
        });
        return true;
    }

    internal static string RecordReleaseRoute(string runnerId, string slotId) =>
        $"/api/session-runners/{runnerId}/workspace-slots/{slotId}/record-release";

    /// <summary>
    /// Review f4c293bc D-1. The operator's way out of <see cref="WorkspaceSlotCodes.ReleaseStuck"/>: stamps
    /// <see cref="AgentTask.RemoteWorkspaceSlotReleasedAt"/> for the task's current slot and generation on the runner it
    /// is bound to, only after the bounded <see cref="WorkspaceSlotCodes.UnknownSlot"/> attempts recorded the stuck
    /// state for the task's current attempt, and never while the task is Dispatched, Working or Blocked (CARD-1649 A-1:
    /// a reset runner may have leased the same slot and generation to a new attempt). Anything else is a refusal; nothing
    /// is sent to a runner and no directory is touched. The audit event names the requester (A-2).
    /// </summary>
    internal static async Task RecordOperatorReleaseAsync(
        AppDbContext db, TimeProvider clock, string runnerId, string slotId, Guid taskId, int generation, string reason,
        string requester, CancellationToken ct)
    {
        var task = await db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("AgentTask", taskId);
        if (!string.Equals(task.RunnerId, runnerId, StringComparison.Ordinal)
            || !string.Equals(task.RemoteWorkspaceSlotId, slotId, StringComparison.Ordinal)
            || (task.RemoteWorkspaceSlotGeneration ?? 0) != generation)
            throw new ConflictException(
                $"Task {taskId} is not bound to workspace slot {slotId} generation {generation} on runner '{runnerId}'.",
                "workspace_slot_release_mismatch");
        if (task.RemoteWorkspaceSlotReleasedAt is not null)
            throw new ConflictException($"The release of {SlotText(task)} is already recorded.", "workspace_slot_release_recorded_already");
        if (HoldingLease.Contains(task.Status))
            throw new ConflictException(
                $"Task {taskId} is {task.Status} on runner '{runnerId}' and holds {SlotText(task)}; its release is never recorded.",
                TaskLiveCode);
        var stuck = StuckPrefix(task);
        if (!await db.AgentTaskEvents.AsNoTracking().AnyAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Warning && e.Detail != null && e.Detail.StartsWith(stuck), ct))
        {
            var anyAttempt = WorkspaceSlotCodes.ReleaseStuck + ": " + SlotText(task) + " task attempt ";
            if (await db.AgentTaskEvents.AsNoTracking().AnyAsync(e => e.AgentTaskId == task.Id
                    && e.Type == AgentTaskEventType.Warning && e.Detail != null && e.Detail.StartsWith(anyAttempt), ct))
                throw new ConflictException(
                    $"{SlotText(task)} was {WorkspaceSlotCodes.ReleaseStuck} for another attempt of task {taskId}, not attempt {task.Attempt}; "
                    + "the current lease is not the stuck one.", AttemptMismatchCode);
            throw new ConflictException(
                $"{SlotText(task)} is not {WorkspaceSlotCodes.ReleaseStuck}; release it on its runner.", "workspace_slot_release_not_stuck");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        task.RemoteWorkspaceSlotReleasedAt = now;
        task.RemoteWorktreePath = null;
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Warning,
            Detail = $"{WorkspaceSlotCodes.ReleaseRecorded}: {AttemptText(task)} on runner '{runnerId}' recorded released by the operator '{requester}': {reason}",
            At = now,
        });
        await db.SaveChangesAsync(ct);
    }

    internal async Task<bool> EligibleAsync(AgentTask task, CancellationToken ct)
    {
        if (!Terminal.Contains(task.Status))
            return false;
        if (task.AgentSessionId is Guid sessionId
            && await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId
                && s.Status != SessionStatus.Stopped && s.Status != SessionStatus.Failed, ct))
            return false;
        if (await db.AgentTaskSyncDebts.AsNoTracking().AnyAsync(d => d.TaskId == task.Id
                && (d.State == AgentTaskSyncDebtState.Pending || d.State == AgentTaskSyncDebtState.Held), ct))
            return false;
        return !await db.AgentTasks.AsNoTracking().AnyAsync(t => t.FollowUpOfTaskId == task.Id && Live.Contains(t.Status), ct);
    }
}

/// <summary>CARD-0824 S1. Runs the release reconciler every minute while <c>Delegation:WorkspaceSlotsEnabled</c> is on.</summary>
public sealed class WorkspaceSlotReleaseHostedService(
    IServiceScopeFactory scopes, IOptions<DelegationSettings> settings, TimeProvider clock,
    ILogger<WorkspaceSlotReleaseHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (settings.Value.WorkspaceSlotsEnabled)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>().ReconcileAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Workspace slot release pass failed");
                }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

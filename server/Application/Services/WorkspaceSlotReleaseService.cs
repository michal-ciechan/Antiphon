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
/// </summary>
public sealed class WorkspaceSlotReleaseService(
    AppDbContext db, RemoteWorkspaceService remote, TimeProvider clock, ILogger<WorkspaceSlotReleaseService> logger)
{
    private static readonly AgentTaskStatus[] Terminal = [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];
    internal const int UnknownSlotAttempts = 3;
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
            PhoneHomeWorkspaceSlotReleaseResponse response;
            try
            {
                response = await remote.ReleaseSlotAsync(task, ct);
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

using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1432 D-5: the tasks whose <c>refs/antiphon/{land,progress}/&lt;task&gt;/</c> refs must stay
/// (investigation §2, with the CARD-1446 D-3 correction: a Ready sync debt is paid, not open, and the
/// D-4 clause: an Active landing whose Cleanup is Abandoned is not protected).
/// A task with no database row is not protected.
/// </summary>
public static class AntiphonRefProtection
{
    private static readonly AgentTaskStatus[] LiveTask =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];
    private static readonly LandRequestState[] LiveRequest =
        [LandRequestState.Queued, LandRequestState.Held, LandRequestState.Running, LandRequestState.NeedsResolution];
    // CARD-1446 D-3: Ready is the paid state; only Pending and Held debts are open.
    private static readonly AgentTaskSyncDebtState[] OpenDebt =
        [AgentTaskSyncDebtState.Pending, AgentTaskSyncDebtState.Held];
    private static readonly WorktreeRetirementState[] LiveRetirement =
    [
        WorktreeRetirementState.Released, WorktreeRetirementState.Claimed,
        WorktreeRetirementState.CommandStarted, WorktreeRetirementState.Partial,
    ];

    /// <summary>The protected subset of <paramref name="taskIds"/>, or the whole protected set when it is null.</summary>
    public static async Task<HashSet<Guid>> QueryAsync(AppDbContext db, IReadOnlyCollection<Guid>? taskIds, CancellationToken ct)
    {
        var ids = taskIds?.Distinct().ToList();
        var protectedIds = new HashSet<Guid>();

        var tasks = db.AgentTasks.AsNoTracking().Where(t => LiveTask.Contains(t.Status));
        if (ids is not null) tasks = tasks.Where(t => ids.Contains(t.Id));
        protectedIds.UnionWith(await tasks.Select(t => t.Id).ToListAsync(ct));

        // A live request protects its task and its recovery source; an unresolved adopt witness protects its task.
        var requests = db.AgentTaskLandRequests.AsNoTracking().Where(r => LiveRequest.Contains(r.State)
            || (r.SourceResolutionState == LandSourceResolutionState.AdvanceStarted && r.RecoveryAdoptedAt == null));
        if (ids is not null)
            requests = requests.Where(r => ids.Contains(r.TaskId)
                || (r.RecoverySourceTaskId != null && ids.Contains(r.RecoverySourceTaskId.Value)));
        foreach (var r in await requests.Select(r => new { r.TaskId, r.RecoverySourceTaskId, r.State }).ToListAsync(ct))
        {
            protectedIds.Add(r.TaskId);
            if (LiveRequest.Contains(r.State) && r.RecoverySourceTaskId is Guid source) protectedIds.Add(source);
        }

        // An interrupted publication or a refused cleanup that can be retried. CARD-1446 D-4: an Abandoned
        // cleanup has left the Publication lane and no longer protects its task.
        var landings = db.AgentTaskLandings.AsNoTracking()
            .Where(o => o.Active && o.Phase != LandPhase.Complete && o.Phase != LandPhase.Refused
                && o.Cleanup != LandCleanupStatus.Abandoned);
        if (ids is not null)
            landings = landings.Where(o => ids.Contains(o.TaskId)
                || (o.RecoverySourceTaskId != null && ids.Contains(o.RecoverySourceTaskId.Value)));
        foreach (var o in await landings.Select(o => new { o.TaskId, o.RecoverySourceTaskId }).ToListAsync(ct))
        {
            protectedIds.Add(o.TaskId);
            if (o.RecoverySourceTaskId is Guid source) protectedIds.Add(source);
        }

        var debts = db.AgentTaskSyncDebts.AsNoTracking().Where(d => OpenDebt.Contains(d.State));
        if (ids is not null) debts = debts.Where(d => ids.Contains(d.TaskId));
        protectedIds.UnionWith(await debts.Select(d => d.TaskId).ToListAsync(ct));

        var retirements = db.TaskWorktreeRetirements.AsNoTracking().Where(r => r.Active && LiveRetirement.Contains(r.State));
        if (ids is not null) retirements = retirements.Where(r => ids.Contains(r.TaskId));
        protectedIds.UnionWith(await retirements.Select(r => r.TaskId).ToListAsync(ct));

        if (ids is not null) protectedIds.IntersectWith(ids);
        return protectedIds;
    }
}

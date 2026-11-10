using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AgentTaskDispatcher
{
    /// <summary>
    /// CARD-1478. Before auth probes, the repository lease, and the claim. An existing warm
    /// process stays outside the gate; a later <see cref="ReuseOutcome.SpawnFresh"/> is rechecked
    /// at the spawn boundary.
    /// </summary>
    private async Task<DispatchOneResult?> HoldBeforeDesktopMemoryClaimAsync(AgentTask task, CancellationToken ct)
    {
        if (!DesktopMemoryApplies(task) || await ExistingProcessCanTakeAsync(task, ct))
            return null;
        var detail = _desktopMemory!.HoldDetail(_settings.DesktopLaunchMemoryFloorMb);
        if (detail is null)
            return null;
        _desktopMemoryHoldDetail = detail;
        return DispatchOneResult.HeldOnMemory;
    }

    private bool DesktopMemoryApplies(AgentTask task) =>
        _desktopMemory is not null
        && _settings.DesktopLaunchMemoryFloorMb > 0
        && task.Kind == AgentTaskKind.Worker
        && !AgentTaskRoles.IsSpecialist(task.Role)
        && string.IsNullOrEmpty(task.RunnerId);

    /// <summary>
    /// Read-only prediction of <see cref="TryReuseWarmAgentAsync"/>. A false positive is recovered
    /// when the real outcome is a fresh process. A false negative would hold a live session, so a
    /// live session and an AlwaysOn standing seat are exempt.
    /// </summary>
    private async Task<bool> ExistingProcessCanTakeAsync(AgentTask task, CancellationToken ct)
    {
        if (task.Workspace != WorkspaceMode.Shared)
            return false;
        if (AgentLaunchEnv.Parse(task.LaunchEnvOverrideJson).Count > 0)
            return false;

        if (task.AgentId is Guid pinnedId)
        {
            var pinned = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pinnedId, ct);
            if (pinned is null)
                return false;
            if (await LiveSessionIdOfAsync(pinned, ct) is not null)
                return true;
            return !pinned.IsPoolDelegate && pinned.AlwaysOn;
        }

        if (!_settings.PoolEnabled)
            return false;

        var reservationCutoff = UtcNow().AddMinutes(-Math.Max(0, _settings.PoolReservedForCallerMinutes));
        var projectEndpoints = await ProjectProviderEndpointsAsync(task.ProjectId, ct);
        var warm = await _db.Agents.AsNoTracking()
            .Where(a => a.IsPoolDelegate
                && a.Status == AgentStatus.Idle
                && a.PoolIdleSince != null
                && a.ModelLevel == task.ModelLevel
                && a.PoolProjectId == task.ProjectId
                && a.Kind == task.AgentKind
                && a.RunnerId == task.RunnerId)
            .ToListAsync(ct);
        foreach (var candidate in warm)
        {
            if (!SameDirectory(candidate.WorkingDirectory, task.WorkingDirectory))
                continue;
            if (IsWorktreeWorkingDirectory(candidate.WorkingDirectory))
                continue;
            if (!InheritedEnvMatchesPool(candidate, task, projectEndpoints))
                continue;
            if (!ClaudeEffortMatchesPool(candidate, task))
                continue;
            if (candidate.PoolReservedForRootTaskId != task.RootTaskId
                && candidate.PoolIdleSince is DateTime idle
                && idle > reservationCutoff)
                continue;
            if (await LiveSessionIdOfAsync(candidate, ct) is not null)
                return true;
        }

        return false;
    }
}

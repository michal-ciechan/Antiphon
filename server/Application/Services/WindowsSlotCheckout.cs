using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0824 S3 D-4. A Windows slot is the registered checkout. Until the runner reports the slot
/// path, <see cref="AgentTask.WorktreePath"/> holds <see cref="PendingPrefix"/> and no
/// <c>card-task-*</c> directory is created. server2 never uses the sentinel.
/// </summary>
public static class WindowsSlotCheckout
{
    public const string PendingPrefix = "windows-slot-pending:";

    public static bool IsPending(string? path) =>
        path?.StartsWith(PendingPrefix, StringComparison.Ordinal) == true;

    public static string Pending(Guid taskId) => PendingPrefix + taskId.ToString("N");

    public static bool IsWindows(RunnerDescriptor? runner)
    {
        if (runner is null)
            return false;
        var platform = RunnerPlatformWire.Normalize(runner.Platform)
            ?? RunnerPlatformWire.Normalize(runner.Capabilities?.Platform);
        return platform == RunnerPlatformWire.Windows;
    }

    /// <summary>
    /// The Windows allow-list verdict. Callers already know the runner is Windows and advertises
    /// <c>workspaceSlotV1</c>, and that the restart-only slot flag is on.
    /// </summary>
    public static WorkspaceSlotDecision DecideListed(DelegationSettings settings, AgentTask task)
    {
        if (!WorkspaceSlotRoles.TryParse(settings.WindowsSlotRoles, out var set, out _))
            return new(false, WorkspaceSlotRoleVerdict.WindowsRolesInvalid);

        var verdict = set.Admits(task.Role, task.SourceLandingOperationId);
        if (verdict.Reason == WorkspaceSlotRoleVerdict.RolesNone)
            return new(false, WorkspaceSlotRoleVerdict.WindowsRolesNone);
        if (!verdict.Lease)
            return new(false, verdict.Reason);

        var id = task.RunnerId ?? "";
        if (string.Equals(id, RunnerPlatformWire.DesktopId, StringComparison.Ordinal))
            return new(false, WorkspaceSlotRoleVerdict.WindowsRunnerDesktop);
        var ids = settings.WindowsSlotRunnerIds ?? [];
        if (!ids.Contains(id, StringComparer.Ordinal))
            return new(false, WorkspaceSlotRoleVerdict.WindowsRunnerNotListed);
        return new(true, verdict.Reason);
    }
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Shared workspace-use fence. Callers pass coordinates; this never holds a row lock across Git.</summary>
public sealed class WorkspaceUseAdmission(
    IWorkspaceReservationJournal journal,
    AppDbContext db,
    ILogger<WorkspaceUseAdmission>? logger = null)
{
    /// <summary>Admits one consumer and returns its reservation (CARD-0664: callers may release it).</summary>
    public async Task<WorkspaceReservationSnapshot> RequireConsumerAsync(WorkspaceReservationCommand command, CancellationToken ct)
    {
        var result = await journal.TryAdmitConsumerAsync(command, ct);
        if (!result.Accepted)
            throw new ConflictException(result.Reason ?? "Workspace is reserved for retirement.", "workspace_reserved");
        return result.Snapshot!;
    }

    /// <summary>
    /// CARD-0664 D-3/D-4: release one admitted reservation (an admitting operation threw after
    /// admission). Best-effort: a failed release is logged and never replaces the caller's error.
    /// </summary>
    public Task ReleaseAsync(WorkspaceReservationSnapshot? snapshot, CancellationToken ct) =>
        snapshot is null
            ? Task.CompletedTask
            : BestEffortAsync(() => journal.ReleaseConsumerAsync(snapshot.Id, snapshot.Generation, ct), "reservation", snapshot.Id);

    /// <summary>CARD-0664 D-2/D-3: best-effort owner-end release after the task's terminal commit.</summary>
    public Task ReleaseTaskConsumersAsync(Guid taskId, CancellationToken ct) =>
        BestEffortAsync(() => journal.ReleaseTaskConsumersAsync(taskId, ct), "task", taskId);

    /// <summary>CARD-0664 D-2/D-3: best-effort owner-end release after the session's terminal commit.</summary>
    public Task ReleaseSessionConsumersAsync(Guid sessionId, CancellationToken ct) =>
        BestEffortAsync(() => journal.ReleaseSessionConsumersAsync(sessionId, ct), "session", sessionId);

    private async Task BestEffortAsync(Func<Task> release, string owner, Guid id)
    {
        try
        {
            await release();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Workspace reservation release failed for {Owner} {OwnerId}", owner, id);
        }
    }

    public async Task InvalidateReleaseAsync(Guid taskId, CancellationToken ct) =>
        await journal.InvalidateUnclaimedReleaseAsync(taskId, ct);

    public async Task FenceCompletedAsync(TaskWorktreeRetirement retirement, CancellationToken ct) =>
        await journal.TryAdmitConsumerAsync(new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(retirement.WorktreePath, retirement.SourceFullRef, retirement.RepositoryPath),
            WorkspaceReservationKind.HistoricalFence, retirement.TaskId, null, retirement.Id), ct);

    public async Task<IReadOnlyList<WorkspaceReservationSnapshot>> FindLiveConsumersAsync(
        WorkspaceReservationKey key, Guid retiringTaskId, CancellationToken ct)
    {
        var active = await journal.ReadActiveAsync(key, liveOwnersOnly: true, ct);
        return active.Where(s => s.Kind != WorkspaceReservationKind.Retirement
            && s.RetirementId != retiringTaskId).ToList();
    }

    public async Task<bool> HasLiveTaskConsumerAsync(string path, string branch, Guid retiringTaskId, CancellationToken ct)
    {
        var live = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != retiringTaskId && Live.Contains(t.Status))
            .Select(t => new { t.Id, t.WorktreePath, t.WorkingDirectory, t.WorktreeBranch })
            .ToListAsync(ct);
        return live.Any(t => Overlaps(t.WorktreePath ?? t.WorkingDirectory, path)
            || string.Equals(BranchName(t.WorktreeBranch), BranchName(branch), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> HasLiveSessionOwnerAsync(string path, CancellationToken ct)
    {
        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping)
            .Select(s => s.Cwd)
            .ToListAsync(ct);
        var agents = await db.Agents.AsNoTracking()
            .Where(a => a.Status == AgentStatus.Running || a.PoolIdleSince != null)
            .Select(a => a.WorkingDirectory)
            .ToListAsync(ct);
        return sessions.Concat(agents).Any(p => !string.IsNullOrWhiteSpace(p) && Overlaps(p, path));
    }

    public bool Overlaps(string? candidate, string path)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return DelegationWorkspaceResolver.IsWithinRoot(candidate, path)
                || DelegationWorkspaceResolver.IsWithinRoot(path, candidate)
                || PathsEqual(candidate, path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static readonly AgentTaskStatus[] Live =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    private static string BranchName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? ""
        : value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0604 D-15. The desktop half of the mirror-worktree design for remote ordinary tasks.
///
/// The DESKTOP worktree stays canonical: it is created exactly as it is today, it is what
/// <see cref="AgentTask.WorktreePath"/> names, and landing, retirement, residue and progress
/// accounting all keep reading it. The branch on origin is the unit of exchange, and the runner
/// holds a working copy of it. Nothing here is ever the only copy of anything:
///
///   create desktop worktree -> push branch -> mirror on runner -> session works and pushes
///   -> settlement fast-forwards the desktop worktree from origin -> land as usual.
///
/// The sync is fast-forward ONLY. A diverged branch or a dirty desktop tree is a settlement
/// warning and the task keeps its report; it is never a reset, because a reset would silently
/// throw away whichever side the operator actually wanted.
/// </summary>
public sealed class RemoteWorkspaceService
{
    private readonly ISessionRunnerDirectory _runners;
    private readonly ILandingGit _git;
    private readonly ILogger<RemoteWorkspaceService> _logger;
    private readonly ITaskProgressGit? _progressGit;
    private readonly IRepositoryMutationLease? _leases;
    private readonly IWorkspaceReservationJournal? _reservations;

    public RemoteWorkspaceService(
        ISessionRunnerDirectory runners,
        ILandingGit git,
        ILogger<RemoteWorkspaceService> logger,
        ITaskProgressGit? progressGit = null,
        IRepositoryMutationLease? leases = null,
        IWorkspaceReservationJournal? reservations = null)
    {
        _runners = runners;
        _git = git;
        _logger = logger;
        _progressGit = progressGit;
        _leases = leases;
        _reservations = reservations;
    }

    /// <summary>CARD-0657 D-3. The whole settlement sync attempt's deadline.</summary>
    public TimeSpan SyncBudget { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>The mirror directory name for a task: the dispatcher's own short form.</summary>
    public static string MirrorName(Guid taskId) => "task-" + taskId.ToString("N")[..8];

    /// <summary>
    /// Pushes the task branch to origin so the runner has something to fetch. A push failure is
    /// NOT a fallback to a local launch: a remote task whose branch never reached origin has no
    /// source to run against, so the caller keeps it Queued with a warning.
    /// </summary>
    public async Task<RemotePushResult> PushBranchAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.WorktreePath) || string.IsNullOrWhiteSpace(task.WorktreeBranch))
            return new RemotePushResult(false, null, "the task has no desktop worktree or branch");

        var head = await _git.RunAsync(task.WorktreePath, ["rev-parse", "HEAD"], ct);
        if (head.ExitCode != 0)
            return new RemotePushResult(false, null, "could not read the desktop worktree HEAD: " + Tail(head.Diagnostic));
        var sha = head.Output.Trim();

        var push = await _git.RunAsync(
            task.WorktreePath, ["push", "-u", "origin", task.WorktreeBranch], ct);
        if (push.ExitCode != 0)
            return new RemotePushResult(false, sha, "git push failed: " + Tail(push.Diagnostic));
        return new RemotePushResult(true, sha, null);
    }

    /// <summary>
    /// Asks the runner for a mirror of the pushed branch at the exact sha. Returns the POSIX path
    /// the session's cwd becomes.
    /// </summary>
    public async Task<string> MirrorAsync(AgentTask task, string sha, CancellationToken ct)
    {
        var client = Remote(task.RunnerId);
        var response = await client.MirrorWorkspaceAsync(
            new PhoneHomeWorkspaceMirrorRequest(task.WorktreeBranch!, sha, MirrorName(task.Id)), ct);
        return response.Path;
    }

    /// <summary>
    /// Settlement sync: bring the canonical desktop worktree up to whatever the session pushed.
    /// Fast-forward only, and never on a dirty tree.
    /// </summary>
    public async Task<RemoteSettlementSyncResult> SyncAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.WorktreePath) || string.IsNullOrWhiteSpace(task.WorktreeBranch))
            return new(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.IdentityMismatch);

        var status = await _git.RunAsync(
            task.WorktreePath, ["status", "--porcelain", "--untracked-files=all"], ct);
        if (status.ExitCode != 0)
            return new(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable);
        if (status.Output.Trim().Length > 0)
            return new(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Dirty);

        var fetch = await _git.RunAsync(task.WorktreePath, ["fetch", "origin", task.WorktreeBranch], ct);
        if (fetch.ExitCode != 0)
            return new(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.FetchUnavailable);

        var merge = await _git.RunAsync(task.WorktreePath, ["merge", "--ff-only", "FETCH_HEAD"], ct);
        if (merge.ExitCode != 0)
            return new(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Diverged);

        var head = await _git.RunAsync(task.WorktreePath, ["rev-parse", "HEAD"], ct);
        return new(RemoteSettlementSyncState.Synchronized,
            DesktopAfterSha: head.ExitCode == 0 ? head.Output.Trim() : null);
    }

    /// <summary>
    /// Removes the runner-side mirror at retirement. Best effort: a mirror that cannot be removed
    /// is recorded as residue on the task for the operator's sweep, never deleted on a guess and
    /// never allowed to fail retirement of the canonical desktop worktree.
    /// </summary>
    public async Task<string?> RemoveMirrorAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.RemoteWorktreePath) || string.IsNullOrWhiteSpace(task.RunnerId))
            return null;
        try
        {
            var response = await Remote(task.RunnerId).RemoveWorkspaceAsync(
                new PhoneHomeWorkspaceRemoveRequest(task.RemoteWorktreePath), ct);
            return response.Removed ? null : response.Residue ?? task.RemoteWorktreePath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Remote mirror {Path} for task {Task} could not be removed", task.RemoteWorktreePath, task.Id);
            return task.RemoteWorktreePath;
        }
    }

    private PhoneHomeRunnerClient Remote(string? runnerId) =>
        _runners.Resolve(runnerId) as PhoneHomeRunnerClient
        ?? throw new InvalidOperationException(
            $"Runner '{runnerId}' did not resolve to a phone-home client; a remote workspace has no meaning locally.");

    private static string Tail(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).ToList();
        return lines.Count == 0 ? "" : lines[^1];
    }
}

public sealed record RemotePushResult(bool Pushed, string? Sha, string? Warning);


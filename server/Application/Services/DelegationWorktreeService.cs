using System.Diagnostics;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The worktree side of a delegated task's life: created at dispatch, merged back when the work
/// succeeds, removed when the merge lands.
///
/// Merging follows the repo convention — rebase then fast-forward, never a merge commit — and a
/// task merges into ITS PARENT'S branch, not master: integration happens once per level of the
/// tree, resolved by the one party (the sub-orchestrator) that knows what each child was supposed
/// to do. A conflict never auto-resolves: the task blocks and a Merge-role delegate gets the
/// conflict list.
/// </summary>
public sealed class DelegationWorktreeService
{
    private readonly IWorktreeManager _worktrees;
    private readonly IGitService _git;
    private readonly ILogger<DelegationWorktreeService> _logger;

    public DelegationWorktreeService(
        IWorktreeManager worktrees,
        IGitService git,
        ILogger<DelegationWorktreeService> logger)
    {
        _worktrees = worktrees;
        _git = git;
        _logger = logger;
    }

    /// <summary>
    /// What happened to a finished Worktree task's branch. Exactly one of these is true, and the
    /// caller acts on which: Merged removes the worktree, Conflicted spawns a Merge task,
    /// LeftForHuman and Failed keep the branch alive and say so in the report.
    /// </summary>
    public sealed record MergeOutcome(
        MergeResult Result,
        IReadOnlyList<string> ConflictFiles,
        string? Detail);

    public enum MergeResult
    {
        /// <summary>Rebased onto the target and fast-forwarded it; the worktree is gone.</summary>
        Merged = 0,

        /// <summary>The delegate wrote nothing — nothing to merge, worktree removed.</summary>
        NothingToMerge = 1,

        /// <summary>Rebase hit conflicts (aborted cleanly); the worktree is intact for a Merge task.</summary>
        Conflicted = 2,

        /// <summary>No merge target was set — the branch is deliberately left for a human.</summary>
        LeftForHuman = 3,

        /// <summary>A git operation failed for a non-conflict reason; branch kept, nothing lost.</summary>
        Failed = 4,
    }

    /// <summary>
    /// Create the task's worktree and record its coordinates on the row. Branches from the merge
    /// target when one is set — the rebase-back is then linear — and from HEAD otherwise. A
    /// leftover worktree from a previous attempt of the SAME task is adopted, not an error.
    /// </summary>
    public async Task CreateForTaskAsync(AgentTask task, CancellationToken ct)
    {
        if (task.RepoPath is not { } repoPath)
            throw new ValidationException(nameof(task.RepoPath), "A worktree task needs a git repository.");

        var identifier = $"task-{DelegationReportFormatter.Short(task.Id)}";
        var baseRef = task.MergeTargetRef ?? "HEAD";

        Dtos.WorktreeInfo info;
        try
        {
            info = await _worktrees.CreateAsync(repoPath, identifier, baseRef, ct);
        }
        catch (ConflictException)
        {
            // A crashed or requeued earlier attempt left its worktree behind. Same task, same
            // branch — reusing it preserves whatever the last attempt committed, which is exactly
            // what the handoff wants.
            var existing = (await _worktrees.ListAsync(repoPath, ct))
                .FirstOrDefault(w => w.CardId == identifier && Directory.Exists(w.Path))
                ?? throw new ConflictException(
                    $"Worktree for task {DelegationReportFormatter.Short(task.Id)} exists but its "
                    + "directory is gone — remove the stale branch and retry.");
            info = existing;
        }

        task.WorktreePath = info.Path;
        task.WorktreeBranch = info.Branch;
        _logger.LogInformation(
            "Task {ShortId}: worktree at {Path} on {Branch} (base {BaseRef})",
            DelegationReportFormatter.Short(task.Id), info.Path, info.Branch, baseRef);
    }

    /// <summary>
    /// Land the task's work on its merge target. Commit-all → rebase onto the target → fast-forward
    /// the target — and remove the worktree only when the target actually moved.
    /// </summary>
    public async Task<MergeOutcome> TryMergeBackAsync(AgentTask task, CancellationToken ct)
    {
        if (task.WorktreePath is not { } worktree || task.WorktreeBranch is not { } branch
            || task.RepoPath is not { } repo)
        {
            return new MergeOutcome(MergeResult.Failed, [], "The task has no worktree recorded.");
        }

        if (!Directory.Exists(worktree))
            return new MergeOutcome(MergeResult.Failed, [], $"Worktree directory is gone: {worktree}");

        try
        {
            // The delegate may have left uncommitted work — a report that says "done" with a dirty
            // tree is normal, not an error. Sweep it into the task branch first.
            await _git.CommitAllChangesAsync(
                worktree, $"task {DelegationReportFormatter.Short(task.Id)}: {task.Title}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MergeOutcome(MergeResult.Failed, [], $"Committing the delegate's work failed: {ex.Message}");
        }

        if (task.MergeTargetRef is not { } target)
            return new MergeOutcome(MergeResult.LeftForHuman, [], $"Branch {branch} kept — no merge target was set.");

        // Nothing on the branch beyond the target means the delegate changed nothing (a read-only
        // investigation, or work it decided against). Don't leave an empty branch around.
        var ahead = await GitAsync(worktree, ct, "rev-list", "--count", $"{target}..HEAD");
        if (ahead.Ok && ahead.StdOut.Trim() == "0")
        {
            await RemoveQuietlyAsync(repo, worktree, ct);
            return new MergeOutcome(MergeResult.NothingToMerge, [], null);
        }

        // Rebase, never merge commits (repo convention). A conflict aborts cleanly: the worktree is
        // left exactly as the delegate finished it, which is what the Merge task needs to see.
        var rebase = await GitAsync(worktree, ct, "rebase", target);
        if (!rebase.Ok)
        {
            var conflicts = await GitAsync(worktree, ct, "diff", "--name-only", "--diff-filter=U");
            var files = conflicts.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            await GitAsync(worktree, ct, "rebase", "--abort");

            if (files.Count > 0)
                return new MergeOutcome(MergeResult.Conflicted, files, rebase.StdErr.Trim());

            return new MergeOutcome(MergeResult.Failed, [], $"Rebase onto {target} failed: {rebase.StdErr.Trim()}");
        }

        var advanced = await AdvanceTargetAsync(repo, branch, target, ct);
        if (advanced is { } failure)
            return new MergeOutcome(MergeResult.Failed, [], failure);

        await RemoveQuietlyAsync(repo, worktree, ct);
        return new MergeOutcome(MergeResult.Merged, [], $"{branch} → {target}");
    }

    /// <summary>
    /// Fast-forward the target ref to the rebased branch. `git fetch . branch:target` is the
    /// primitive — it refuses anything but a fast-forward and needs no checkout. When the target IS
    /// checked out somewhere (the parent's own worktree, or the main repo), that refusal is
    /// expected: fall back to `merge --ff-only` inside that checkout, which also updates its
    /// working tree — the parent sees the child's work appear, which is the point of merging into
    /// the parent's branch. Returns null on success, or the failure detail.
    /// </summary>
    private async Task<string?> AdvanceTargetAsync(
        string repo, string branch, string target, CancellationToken ct)
    {
        var fetch = await GitAsync(repo, ct, "fetch", ".", $"{branch}:{target}");
        if (fetch.Ok)
            return null;

        var checkout = await FindCheckoutOfBranchAsync(repo, target, ct);
        if (checkout is null)
            return $"Could not fast-forward {target} to {branch}: {fetch.StdErr.Trim()}";

        var merge = await GitAsync(checkout, ct, "merge", "--ff-only", branch);
        return merge.Ok
            ? null
            : $"Could not fast-forward {target} (checked out at {checkout}): {merge.StdErr.Trim()}";
    }

    /// <summary>Where <paramref name="branch"/> is currently checked out, if anywhere.</summary>
    private async Task<string?> FindCheckoutOfBranchAsync(string repo, string branch, CancellationToken ct)
    {
        var list = await GitAsync(repo, ct, "worktree", "list", "--porcelain");
        if (!list.Ok)
            return null;

        string? currentPath = null;
        foreach (var line in list.StdOut.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                currentPath = line["worktree ".Length..];
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal)
                && line["branch refs/heads/".Length..] == branch)
                return currentPath;
        }
        return null;
    }

    private async Task RemoveQuietlyAsync(string repo, string worktree, CancellationToken ct)
    {
        try
        {
            await _worktrees.RemoveAsync(repo, worktree, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The janitor's TTL prune is the backstop; a lingering merged worktree costs disk, not
            // correctness.
            _logger.LogWarning(ex, "Could not remove merged worktree {Path}; the janitor will", worktree);
        }
    }

    private sealed record GitResult(bool Ok, string StdOut, string StdErr);

    private static async Task<GitResult> GitAsync(string workingDirectory, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new GitResult(process.ExitCode == 0, await stdout, await stderr);
    }
}

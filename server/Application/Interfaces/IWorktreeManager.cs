using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeManager
{
    Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct);

    Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct);

    Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct);

    /// <summary>
    /// Non-throwing cleanup. Default implementation calls <see cref="RemoveAsync"/> and reports
    /// clean so existing test fakes compile unchanged. <paramref name="mergedInto"/> is ignored
    /// by the default; the real manager honours it (ancestor-guarded <c>branch -D</c>).
    /// </summary>
    async Task<WorktreeRemoval> TryRemoveAsync(
        string repoPath, string worktreePath, string? mergedInto, CancellationToken ct)
    {
        await RemoveAsync(repoPath, worktreePath, ct);
        return WorktreeRemoval.Clean;
    }

    Task TouchAsync(string worktreePath, CancellationToken ct);

    Task<int> PruneStaleAsync(CancellationToken ct);

    /// <summary>
    /// CARD-0147 S3: porcelain plus <c>git branch --list feat/card-task-*</c> for one repo.
    /// Default empty so existing test fakes compile unchanged. Detection only — never prune.
    /// </summary>
    Task<IReadOnlyList<DelegateWorktreeScanEntry>> ScanDelegateWorktreesAsync(
        string repoPath, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DelegateWorktreeScanEntry>>([]);

    /// <summary>
    /// Repo paths recorded in <c>.antiphon/worktrees/*.json</c> under the worktree base.
    /// Default empty so existing test fakes compile unchanged.
    /// </summary>
    Task<IReadOnlyList<string>> ListKnownDelegateRepoPathsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>
    /// CARD-0328 S3: registered worktrees under <c>Git:WorktreeBasePath</c>, leftover
    /// <c>card-task-*</c> directories, and local <c>feat/card-task-*</c> branches.
    /// <paramref name="extraRepoPaths"/> are task <c>RepoPath</c> values the metadata may miss.
    /// Default empty so existing test fakes compile unchanged.
    /// </summary>
    Task<IReadOnlyList<WorktreeResidueScanEntry>> ScanResidueCandidatesAsync(
        IReadOnlyList<string> extraRepoPaths, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WorktreeResidueScanEntry>>([]);

    /// <summary>
    /// CARD-0328 S3: ancestor/ahead and porcelain dirtiness for one residue candidate.
    /// A missing repo, branch, or directory degrades to "no branch, ancestor, clean".
    /// Default so existing test fakes compile unchanged.
    /// </summary>
    Task<WorktreeResidueGitState> InspectResidueAsync(
        string? repoPath, string? worktreePath, string? branch, string targetRef, CancellationToken ct)
        => Task.FromResult(new WorktreeResidueGitState(false, true, 0, false, false));
}

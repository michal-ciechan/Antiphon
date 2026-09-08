using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeManager
{
    Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct);

    Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct);

    Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct);

    /// <summary>Legacy callers have no deletion authority; retain their residue.</summary>
    Task<WorktreeRemoval> TryRemoveAsync(
        string repoPath, string worktreePath, string? mergedInto, CancellationToken ct)
        => Task.FromResult(new WorktreeRemoval(false, false, false, "typed_removal_authority_required"));

    Task<WorktreeRemoval> TryRemoveAsync(WorktreeRemovalRequest request, CancellationToken ct)
        => Task.FromResult(new WorktreeRemoval(false, false, false, "guarded_removal_not_implemented"));

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
    /// Missing or failed inspection has no cleanup authority.
    /// Default so existing test fakes compile unchanged.
    /// </summary>
    Task<WorktreeResidueGitState> InspectResidueAsync(
        string? repoPath, string? worktreePath, string? branch, string targetRef, CancellationToken ct)
        => Task.FromResult(new WorktreeResidueGitState(false, false, 0, true, true));
}

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
}

using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeManager
{
    Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct);

    Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct);

    Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct);

    Task TouchAsync(string worktreePath, CancellationToken ct);

    Task<int> PruneStaleAsync(CancellationToken ct);
}

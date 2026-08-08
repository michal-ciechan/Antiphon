namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Git identity of one working directory: which repo it belongs to and what is checked out
/// there. <see cref="RepoRoot"/> is the MAIN checkout's root — for a linked worktree (or a
/// subdirectory of either) it points back at the primary repo, which is how the home screen
/// nests worktree- and subdirectory-scoped agents under the project they belong to.
/// </summary>
public sealed record WorkspaceGitInfoDto(
    string Path,
    bool IsGitRepository,
    string? RepoRoot,
    string? Branch,
    bool IsWorktree);

/// <summary>Every worktree of the repo containing <see cref="Path"/> (main checkout included).</summary>
public sealed record WorkspaceWorktreesDto(
    string Path,
    bool IsGitRepository,
    string? RepoRoot,
    IReadOnlyList<WorktreeEntryDto> Worktrees);

public sealed record WorktreeEntryDto(
    string Path,
    // Null when detached — the UI shows the path and no branch badge.
    string? Branch,
    bool IsMain,
    bool IsLocked = false,
    bool IsDetached = false);

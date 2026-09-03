namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-0147 S3: one <c>feat/card-task-*</c> worktree or dangling branch as git sees it.
/// <see cref="Registered"/> is false for a branch with no worktree.
/// </summary>
public sealed record DelegateWorktreeScanEntry(
    string RepoPath,
    string Path,
    string Branch,
    string ShortId,
    bool Registered,
    bool Locked,
    string? LockReason,
    bool DirectoryExists,
    bool GitFileExists);

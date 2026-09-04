using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0328 S3: first-match-wins residue labels.</summary>
public enum WorktreeResidueLabel
{
    Unknown = 0,
    Live = 1,
    Settling = 2,
    Unmerged = 3,
    Dirty = 4,
    Eligible = 5
}

/// <summary>
/// One registered worktree, leftover <c>card-task-*</c> directory, or local
/// <c>feat/card-task-*</c> branch as git/filesystem sees it.
/// </summary>
public sealed record WorktreeResidueScanEntry(
    string Path,
    string Branch,
    string? RepoPath,
    bool Registered,
    bool DirectoryExists);

/// <summary>Git facts for one residue candidate. Missing repo/branch/dir degrades to zeros.</summary>
public sealed record WorktreeResidueGitState(
    bool BranchExists,
    bool IsAncestor,
    int AheadCount,
    bool HasTrackedModifications,
    bool HasUntrackedModifications);

/// <summary>Read-only <c>AgentTasks</c> projection used by the residue classifier.</summary>
public sealed record WorktreeResidueTaskSnapshot(
    Guid Id,
    AgentTaskStatus Status,
    DateTime? CompletedAt,
    string? WorktreePath,
    string? WorktreeBranch,
    string? MergeTargetRef,
    string? RepoPath,
    IReadOnlyList<AgentTaskEventType> EventTypes);

/// <summary>Classifier input: one candidate plus optional task and git facts.</summary>
public sealed record WorktreeResidueFacts(
    string Path,
    string Branch,
    string? RepoPath,
    string TargetRef,
    WorktreeResidueTaskSnapshot? Task,
    bool DirectoryExists,
    bool BranchExists,
    bool IsAncestor,
    int AheadCount,
    bool HasTrackedModifications,
    bool HasUntrackedModifications);

public sealed record WorktreeResidueRow(
    string Path,
    string Branch,
    string? RepoPath,
    string TargetRef,
    Guid? TaskId,
    AgentTaskStatus? TaskStatus,
    WorktreeResidueLabel Label,
    string DisplayLabel,
    string Detail,
    bool Keep,
    string? Residue = null);

public sealed record WorktreeResidueCounts(
    int Unknown,
    int Live,
    int Settling,
    int Unmerged,
    int Dirty,
    int Eligible,
    int Kept,
    int Removed);

public sealed record WorktreeResidueResult(
    DateTimeOffset GeneratedAtUtc,
    TimeSpan Duration,
    bool Execute,
    WorktreeResidueCounts Counts,
    IReadOnlyList<WorktreeResidueRow> Rows,
    IReadOnlyList<WorktreeResidueRow> Kept,
    int Removed);

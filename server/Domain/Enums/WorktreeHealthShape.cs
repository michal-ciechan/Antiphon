namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0147 S3: a detected git/tracking mismatch for a delegate branch (legacy or card/stage form) worktree or branch.
/// Detection only — nothing here is pruned, healed, cancelled, or re-dispatched.
/// </summary>
public enum WorktreeHealthShape
{
    /// <summary>Registered and locked (often <c>initializing</c>) with the directory gone or no <c>.git</c> file.</summary>
    LockedMissing = 1,

    /// <summary>Matched task is still Queued/Dispatched/Working and the worktree is locked or missing.</summary>
    OpenTaskUnhealthy = 2,

    /// <summary>Registered delegate branch (legacy or card/stage form) worktree with no <c>AgentTask</c> row.</summary>
    RegisteredNoTask = 3,

    /// <summary>delegate branch (legacy or card/stage form) branch, no worktree, no task row.</summary>
    DanglingBranchNoTask = 4,
}

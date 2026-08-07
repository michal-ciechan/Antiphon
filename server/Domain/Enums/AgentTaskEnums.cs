namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// The one structural choice made when delegating: a <see cref="Worker"/> does a piece of work and
/// reports, an <see cref="Orchestrator"/> owns a chunk and runs its own agents. Nothing in the
/// system decomposes work automatically — a human or an agent picks this.
/// </summary>
public enum AgentTaskKind
{
    Worker = 0,

    /// <summary>
    /// A sub-orchestrator: gets the orchestrator contract, the delegate skill, and a token with
    /// the create scope. Its report is a ROLLUP of its subtree, not a work product.
    /// </summary>
    Orchestrator = 1,
}

/// <summary>
/// What the work IS. Maps to a model tier via <c>DelegationSettings.RolePolicy</c> — that mapping
/// is the whole cost decision, so it lives in config rather than here.
/// </summary>
public enum AgentTaskRole
{
    Custom = 0,
    Plan = 1,
    Code = 2,
    Review = 3,
    Debug = 4,
    Coverage = 5,
    Docs = 6,
    Commit = 7,
    Test = 8,
    Deploy = 9,
    /// <summary>Resolve a merge conflict left behind by a Worktree task.</summary>
    Merge = 10,
}

public enum AgentTaskStatus
{
    Queued = 0,
    Dispatched = 1,
    Working = 2,
    /// <summary>The delegate asked a question — it needs an answer, not a retry.</summary>
    Blocked = 3,
    Succeeded = 4,
    Failed = 5,
    Canceled = 6,
}

/// <summary>
/// Where the delegate runs. <see cref="Shared"/> is the DEFAULT: most delegated work either must
/// see live state (deploys, test runs, log reads) or is small enough that a worktree's branch +
/// merge-back + conflict path is pure overhead. Isolation is opt-in.
/// </summary>
public enum WorkspaceMode
{
    /// <summary>Runs directly in the task's working directory, no isolation.</summary>
    Shared = 0,

    /// <summary>A fresh git worktree on a task branch, merged back into the parent's branch.</summary>
    Worktree = 1,

    /// <summary>Shared directory, but the brief says don't write.</summary>
    ReadOnly = 2,
}

/// <summary>Where the delegate's report goes when the task settles.</summary>
public enum AgentTaskReplyTo
{
    /// <summary>Nowhere — the result lands on the board only (the manual entry point's default).</summary>
    None = 0,

    /// <summary>Delivered into the parent agent session that created the task.</summary>
    Session = 1,
}

public enum AgentTaskEventType
{
    Created = 0,
    Dispatched = 1,
    Escalated = 2,
    Blocked = 3,
    Replied = 4,
    Merged = 5,
    Conflicted = 6,
    Retried = 7,
    Completed = 8,
    Failed = 9,
    Canceled = 10,
    Rejected = 11,
}

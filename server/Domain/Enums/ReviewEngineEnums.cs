namespace Antiphon.Server.Domain.Enums;

// Review engine enums (docs/features/012-review-engine/api.md). The C# names carry a Review prefix
// where a bare name would collide (GateDecision is an entity); the serialised member names are the
// contract's PascalCase strings.

public enum ReviewHost
{
    GitHub,
    Local,
}

public enum ReviewRunStatus
{
    Queued,
    Running,
    AwaitingFixes,
    AtGate,
    Approved,
    AwaitingMerge,
    Merged,
    Closed,
    Failed,
}

public enum ReviewStage
{
    Intake,
    Categorise,
    Select,
    Review,
    Collate,
    Verify,
    Publish,
    AwaitingFixes,
    HumanGate,
    Approval,
    BranchMaintenance,
    Done,
}

public enum ReviewNodeKind
{
    Code,
    Decision,
    Completion,
    Agent,
    FanOut,
    Join,
    Gate,
    Wait,
}

public enum ReviewNodeStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Waiting,
    NotReached,

    /// <summary>The step failed, and its <c>onFailure: continue</c> policy let the run go on (with a fallback where it has one).</summary>
    FailedContinued,
}

public enum ReviewSeverity
{
    Nit,
    Minor,
    Major,
    Blocker,
}

public enum ReviewFindingStatus
{
    Open,
    WillFix,
    Fixed,
    Disputed,
    Dismissed,
    Deferred,
    Outdated,
    Accepted,
}

public enum ReviewSyncState
{
    NotApplicable,
    Pending,
    Synced,
    Failed,
}

public enum ReviewFindingAction
{
    WillFix,
    Fixed,
    Dismiss,
    Dispute,
    Question,
    Defer,
    NotABlocker,
    Reply,
}

public enum ReviewReasonCategory
{
    FalsePositive,
    ByDesign,
    OutOfScope,
    AcceptedRisk,
    FixLater,
    Duplicate,
    StylePreference,
}

public enum ReviewDismissScope
{
    Instance,
    Path,
    Repo,
    Team,
}

public enum ReviewGateKind
{
    ReviewGate,
    ApprovalGate,
    DisputedFindings,
    LearnProposal,

    /// <summary>After the approval: a person confirms the merge (design 4.12). Optional, off per repo.</summary>
    MergeGate,
}

public enum ReviewGateDecision
{
    Approve,
    AnotherRound,
    Close,
}

public enum ReviewQueueItemStatus
{
    Unassigned,
    Assigned,
    InProgress,
    Done,
}

public enum ReviewTransport
{
    Http,
    Seat,
    OneShot,
}

public enum ReviewWaitingOn
{
    Author,
    Humans,
    Bot,
}

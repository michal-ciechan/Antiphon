using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

// Review engine persistence, first slice (docs/features/012-review-engine/design.md section 18,
// api.md). One subject per PR or commit range; one long-lived run per subject.

/// <summary>What is reviewed: a PR on a code host, or a local commit range.</summary>
public sealed class ReviewSubject
{
    public Guid Id { get; set; }
    public ReviewHost Host { get; set; }

    /// <summary><c>owner/name</c>.</summary>
    public string Repo { get; set; } = "";

    public int? PrNumber { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string AuthorLogin { get; set; } = "";
    public string? AuthorDisplayName { get; set; }

    /// <summary>The account that opened the PR; differs from <see cref="AuthorLogin"/> when the author was resolved.</summary>
    public string OpenedByLogin { get; set; } = "";

    /// <summary>How the author was resolved when the PR author was a service account (design 11.5).</summary>
    public string? AuthorResolvedBy { get; set; }

    public string BaseRef { get; set; } = "";
    public string HeadRef { get; set; } = "";
    public string BaseSha { get; set; } = "";
    public string HeadSha { get; set; } = "";

    /// <summary>Code-owner teams of the changed paths (design 11.4).</summary>
    public List<string> OwnerTeams { get; set; } = [];

    /// <summary>Code-owner users of the changed paths.</summary>
    public List<string> OwnerUsers { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}

/// <summary>A review run: one per pull request until Closed (design 3.2).</summary>
public sealed class ReviewRun
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public ReviewSubject Subject { get; set; } = null!;
    // Repository names are display data. The legacy slug is used only for unverifiable historical rows.
    public string? GitHubRepo { get; set; }
    public int? GitHubPrNumber { get; set; }
    public string? GitHubAuthority { get; set; }
    public long? GitHubRepositoryId { get; set; }
    public bool IsDemo { get; set; }
    public uint ConcurrencyToken { get; private set; }
    public ReviewRunStatus Status { get; set; }
    public ReviewStage Stage { get; set; }
    public int Round { get; set; } = 1;
    public int BotOnlyRounds { get; set; }

    /// <summary>Sticky once true (design 4.10); null until the round reaches the approval check.</summary>
    public bool? NeedsHumanApproval { get; set; }

    public ReviewWaitingOn? WaitingOn { get; set; }
    public decimal CostUsd { get; set; }
    public List<string> Categories { get; set; } = [];

    // Blast radius (design 4.3), 1-5 each; all null until categorised.
    public int? BlastReach { get; set; }
    public int? BlastExternalContract { get; set; }
    public int? BlastData { get; set; }
    public int? BlastRollbackAtRelease { get; set; }
    public int? BlastReversalAfterMonths { get; set; }
    public int? BlastExposure { get; set; }

    public string WorkflowName { get; set; } = "pr-review";
    public Guid? WorkflowRevisionId { get; set; }

    /// <summary>SHA-256 of the workflow definition this run started on (design 3.3).</summary>
    public string? WorkflowHash { get; set; }

    /// <summary>The steer prompt carried into the next round by a gate's another-round decision.</summary>
    public string? SteerPrompt { get; set; }

    /// <summary>
    /// The base branch tip the engine last saw: pinned at intake, moved by base-moved, record-drift
    /// and base-drift. Refresh turns a different tip into a <c>base-moved</c> event (design 8.5).
    /// </summary>
    public string? BaseTipSha { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ReviewIteration> Iterations { get; set; } = [];
    public List<ReviewNodeExecution> Nodes { get; set; } = [];
    public List<ReviewFinding> Findings { get; set; } = [];
    public List<ReviewGate> Gates { get; set; } = [];
}

/// <summary>One pass round the workflow (design 3.2): what started it and in which round.</summary>
public sealed class ReviewIteration
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Iteration { get; set; }
    public int Round { get; set; }

    /// <summary><c>opened</c>, <c>push</c>, <c>recheck</c>, <c>another-round</c>, <c>base-moved</c>, <c>manual</c>.</summary>
    public string Trigger { get; set; } = "";

    /// <summary>The base SHA intake pinned for this iteration (design 4.1); null until intake ran through a code host.</summary>
    public string? BaseSha { get; set; }

    /// <summary>
    /// The head SHA intake pinned for this iteration. The next iteration's delta reviews the change
    /// since this head (design 4.2). Null until intake ran through a code host.
    /// </summary>
    public string? HeadSha { get; set; }

    public DateTime StartedAt { get; set; }
}

/// <summary>A resolved workflow definition a run snapshots (design 3.3), stored as the graph JSON.</summary>
public sealed class ReviewWorkflowRevision
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>SHA-256 of the definition, lower-case hex; unique.</summary>
    public string Hash { get; set; } = "";

    public string GraphJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

/// <summary>One execution of a workflow node in one iteration (design 3.4).</summary>
public sealed class ReviewNodeExecution
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string NodeId { get; set; } = "";
    public int Iteration { get; set; }

    /// <summary>
    /// Creation order within the run. The run's next step follows the latest top-level execution of
    /// its current iteration, so a node may run more than once in one iteration (a wait re-entered
    /// after <c>record-drift</c>).
    /// </summary>
    public int Sequence { get; set; }

    public ReviewNodeKind Kind { get; set; }
    public ReviewNodeStatus Status { get; set; }

    /// <summary>The fan-out item, for example the reviewer group <c>security</c>.</summary>
    public string? ItemKey { get; set; }

    /// <summary>The outgoing edge label taken, for example <c>clean</c> or <c>moved</c>.</summary>
    public string? Outcome { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public ReviewTransport? Transport { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }

    /// <summary><c>actual</c> or <c>api-equivalent</c> (D-20).</summary>
    public string? CostBasis { get; set; }

    public string? InputsHash { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Summary { get; set; }

    /// <summary>The node's outputs as canonical JSON (design 3.4): selected checks, groups, a reviewer's findings, a verdict.</summary>
    public string? OutputJson { get; set; }
}

/// <summary>One reviewer group's context pack (design 4.6): the text the reviewer is given, and its hashes.</summary>
public sealed class ReviewContextPack
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Iteration { get; set; }
    public string GroupKey { get; set; } = "";

    /// <summary>SHA-256 of <see cref="Content"/>, lower-case hex.</summary>
    public string Hash { get; set; } = "";

    /// <summary>SHA-256 of the shared prefix, identical across the iteration's packs.</summary>
    public string PrefixHash { get; set; } = "";

    public string Content { get; set; } = "";
    public int TokenEstimate { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A context-pack ledger row (design 4.6): a file considered for the shared prefix, why, and whether it fit.</summary>
public sealed class ReviewPackLedgerEntry
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Iteration { get; set; }
    public string Path { get; set; } = "";

    /// <summary><c>diff</c> today; the impact index, callers, co-change and the rest come later.</summary>
    public string Source { get; set; } = "";

    public string Reason { get; set; } = "";
    public double Score { get; set; }
    public int Tokens { get; set; }
    public bool Included { get; set; }
}

/// <summary>A finding raised by a reviewer (design 4.7, ABF review-findings/1).</summary>
public sealed class ReviewFinding
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    /// <summary>The run-local key, <c>F1</c>.</summary>
    public string Key { get; set; } = "";

    public string? CheckId { get; set; }

    /// <summary>The check's <c>@humans</c> text.</summary>
    public string? CheckWhy { get; set; }

    public ReviewSeverity Severity { get; set; }
    public List<string> Flags { get; set; } = [];
    public bool BypassesRoundBar { get; set; }
    public double Confidence { get; set; }
    public string FilePath { get; set; } = "";
    public int? LineStart { get; set; }
    public int? LineEnd { get; set; }
    public string Anchor { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Suggestion { get; set; }

    /// <summary>A unified-diff hunk around the anchor at the reviewed head.</summary>
    public string? Hunk { get; set; }

    public ReviewFindingStatus Status { get; set; }
    public int RoundRaised { get; set; } = 1;
    public bool Posted { get; set; }
    public ReviewSyncState SyncState { get; set; }
    public string? ExternalThreadUrl { get; set; }

    /// <summary>The code-host comment that opened the finding's thread; replies go to it.</summary>
    public string? ExternalCommentId { get; set; }

    /// <summary>The code-host thread id that resolves it, when the host gave one.</summary>
    public string? ExternalThreadId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ReviewFindingEvent> Events { get; set; } = [];
}

/// <summary>A human response to a finding (design 6). Append-only.</summary>
public sealed class ReviewFindingEvent
{
    public Guid Id { get; set; }
    public Guid FindingId { get; set; }
    public ReviewFindingAction Action { get; set; }
    public string ActorLogin { get; set; } = "";
    public string? ActorDisplayName { get; set; }
    public ReviewReasonCategory? ReasonCategory { get; set; }
    public ReviewDismissScope? Scope { get; set; }
    public string? Text { get; set; }
    public ReviewSyncState SyncState { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A human gate and its queue item (design 11.3).</summary>
public sealed class ReviewGate
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public ReviewGateKind Kind { get; set; }

    /// <summary>The workflow gate node, for example <c>human-approve</c>.</summary>
    public string? NodeId { get; set; }

    public int Iteration { get; set; }
    public ReviewQueueItemStatus Status { get; set; }
    public string? TeamKey { get; set; }
    public string? AssigneeLogin { get; set; }
    public string? AssigneeDisplayName { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public ReviewGateDecision? Decision { get; set; }
    public string? DecidedBy { get; set; }
    public string? SteerPrompt { get; set; }

    /// <summary>Why a human is needed (the fired approval rules, design 4.10).</summary>
    public List<string> Reasons { get; set; } = [];

    /// <summary>"Not me" passes, in order; random assignment skips these logins.</summary>
    public List<ReviewGatePass> Passes { get; set; } = [];
}

/// <summary>One "not me" on a gate (design 11.3).</summary>
public sealed class ReviewGatePass
{
    public Guid Id { get; set; }
    public Guid GateId { get; set; }
    public string Login { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime At { get; set; }
}

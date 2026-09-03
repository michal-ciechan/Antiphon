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

    /// <summary>
    /// Interpret one check-in bundle (CARD-0047 slice 4). Not work anyone delegates by hand: the
    /// check worker creates these, pinned to the standing check interpreter, and reads the answer
    /// off <see cref="Antiphon.Server.Domain.Entities.AgentTask.Result"/>.
    ///
    /// <para>The role exists so three carve-outs can key on something structural rather than on a
    /// naming convention: these rows are hidden from the delegations board by default (there is one
    /// per interpreted check and none of them is anybody's work), they bypass the concurrent-task
    /// cap (a pinned task delivered into an already-running session spawns no process), and they are
    /// never armed for or selected by the check sweep — a check that checked a check would
    /// recurse.</para>
    /// </summary>
    Check = 11,
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
    /// <summary>Something legal but risky — an orchestrator sharing its caller's directory.</summary>
    Warning = 12,

    /// <summary>
    /// A scheduled check-in ran (CARD-0047). Records the digest's head so the drawer shows what the
    /// caller was told and when — a check is an observation, never a state change, so this is the
    /// only trace it leaves on the task.
    /// </summary>
    Check = 13,

    /// <summary>
    /// The caller refined the task mid-flight (CARD-0062) — a message to a delegate that is already
    /// working, or an amendment folded into a still-queued brief. Records what the delegate was told
    /// and when, so a report that diverges from the original brief reads as steered, not off-piste.
    /// Never a state change: the task keeps working.
    /// </summary>
    Refined = 14,

    /// <summary>
    /// The marked turn was killed by a retryable API-error stub (CARD-0072 S5a-3). The task stays
    /// Working with a resume scheduled; this event names the class and the fire time. Written
    /// exactly once per stub — the ApiErrorRecovery row is the idempotency marker that stops
    /// SettleDeferredReportsAsync from re-entering the defer arm forever.
    /// </summary>
    ApiErrorDeferred = 15,

    /// <summary>
    /// The caller already read this completion report through a status poll, so the queued note was
    /// reduced to a pointer while preserving its caller-facing header and warning.
    /// </summary>
    NoteShrunk = 16,

    /// <summary>A digest channel was told this blocked task needs a human answer.</summary>
    HumanNotified = 17,

    /// <summary>
    /// The dispatcher declined to start this task because its declared scope intersects a running
    /// task's (CARD-0063). Written ONCE, on the tick the hold begins — a re-hold on the next tick
    /// is silent, and a hold that resolves is not an event (the dispatch is). Before this the wait
    /// was invisible: the one real hold in 623 tasks left nothing behind but its own queue time.
    /// </summary>
    Held = 18,

    /// <summary>
    /// The task touched an area (or an unmapped path) its declared scope did not cover
    /// (CARD-0063 S4). Recorded at settlement, from the same git-diff-vs-baseline data the Files
    /// review surface already merges. NEVER a state change and never a block: a drift that recurs
    /// is either a caller who should declare that area too, or a map missing a path.
    /// </summary>
    ScopeDrift = 19,

    /// <summary>The caller explicitly queued this finished Worktree task for server-side landing.</summary>
    LandRequested = 20,

    /// <summary>The server rebased, verified, fast-forwarded, pushed, and cleaned up the task branch.</summary>
    Landed = 21,

    /// <summary>
    /// A requested land operation stopped because the target did not advance (fetch, remote-ahead,
    /// rebase, verify, fast-forward, or push failed). The task branch and worktree are retained so
    /// a follow-up delegate can continue from facts. Cleanup failure after a successful push is
    /// <see cref="LandedWithResidue"/>, not this.
    /// </summary>
    LandRefused = 22,

    /// <summary>
    /// The task's (kind, level) was re-chosen from a complexity chain (or later a multi-candidate
    /// pin) because the previous snapshot could not run (CARD-0090). Never a silent guess past
    /// the listed candidates. Appended after shipped 22; do not renumber.
    /// </summary>
    Rerouted = 23,

    /// <summary>
    /// The target advanced and the push succeeded; the branch and/or directory could not be fully
    /// removed (CARD-0328). The task stays Succeeded — residue is a fact about the repo. Re-run
    /// <c>-Land</c> to retry cleanup.
    /// </summary>
    LandedWithResidue = 24,
}

/// <summary>
/// Caller-declared work hardness for a complexity chain (CARD-0090). Distinct from
/// <see cref="AgentModelLevel"/> — they share the word Medium on purpose (the requester's word)
/// but are different JSON fields and different script parameters (<c>-Complexity Medium</c> vs
/// <c>-Level Medium</c>).
/// </summary>
public enum TaskComplexity
{
    Hard = 0,
    Medium = 1,
    Easy = 2,
}

/// <summary>
/// How a settled report was classified (CARD-0159). Legacy is the column default so every
/// pre-existing row is labelled rather than guessed; new settlements always write a non-Legacy
/// value. Never a new <see cref="AgentTaskStatus"/> — Succeeded with UnmarkedAfterNudge is still
/// Succeeded to every consumer of <c>IsSettled</c>.
/// </summary>
public enum AgentTaskReportEvidence
{
    /// <summary>Row predates CARD-0159 — no evidence class was recorded.</summary>
    Legacy = 0,

    /// <summary>The report closed with <c>[antiphon-report:id done|blocked|failed]</c>.</summary>
    Marked = 1,

    /// <summary>
    /// No closing line; settled after a second unmarked end after the delivered nudge
    /// (or a dead session skipped the nudge) (CARD-0248).
    /// </summary>
    UnmarkedAfterNudge = 2,

    /// <summary>No closing line; last-two-lines <c>?</c> heuristic (kept from before CARD-0159).</summary>
    QuestionHeuristic = 3,

    /// <summary>CARD-0046: the turn-ending response never wrote its own text; settled on the join.</summary>
    FinalMessageMissing = 4,

    /// <summary><see cref="AgentTaskRole.Check"/> — the nudge is skipped; the check interpreter has its own format.</summary>
    Exempt = 5,

    /// <summary>
    /// No closing line; the one nudge was issued; the session stayed idle on that same
    /// boundary past <c>UnmarkedWaitingMinutes</c> (CARD-0294). Distinct from
    /// <see cref="QuestionHeuristic"/> (trailing <c>?</c>) and <see cref="UnmarkedAfterNudge"/>
    /// (a later unmarked end settled Succeeded).
    /// </summary>
    UnmarkedWaiting = 6,
}

/// <summary>
/// Durable, machine-readable class of a task failure (CARD-0256). Null on every existing and
/// otherwise-unclassified failure — prose in <c>FailureReason</c> stays the human record.
/// The repeat-dispatch guard keys on this rather than parsing that prose.
/// </summary>
public enum AgentTaskFailureCode
{
    /// <summary>
    /// The bound session reached <see cref="SessionStatus.Stopped"/> with zero transcript
    /// entries and no operator-stop source. Antiphon observed no prompt; the reason names the
    /// recorded <c>TerminationSource</c> when one exists, and says "not recorded" only for
    /// <see cref="SessionTerminationSource.Unknown"/>.
    /// </summary>
    StoppedBeforeFirstPrompt = 0,

    /// <summary>
    /// Structural authentication failure: a 401 turn-kill (CARD-0286) or a provider
    /// sign-in screen at launch (CARD-0324); never a retryable transport glitch.
    /// </summary>
    AuthenticationRequired = 1,

    /// <summary>
    /// A Code-role Worktree task reported <c>done</c> but the isolated worktree had no
    /// post-dispatch commit and no changed or untracked files (CARD-0286). Objective zero
    /// progress, not a guess about why the delegate stopped.
    /// </summary>
    CompletedWithoutProgress = 2,
}

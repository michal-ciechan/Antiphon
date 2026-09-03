using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Create a delegated task. Sent by the delegate script (agent-invoked) or the UI (manual) — the
/// caller's identity comes from the bearer token, never from the body, so a caller cannot claim to
/// be someone else's parent.
/// </summary>
public sealed record CreateAgentTaskRequest(
    string Goal,
    string? Title = null,
    AgentTaskKind Kind = AgentTaskKind.Worker,
    AgentTaskRole Role = AgentTaskRole.Custom,
    /// <summary>Explicit tier override; null takes the role policy's tier.</summary>
    AgentModelLevel? ModelLevel = null,
    /// <summary>
    /// WHICH AGENT PROGRAM runs it (CARD-0084). Null takes the role policy's <c>Kind</c>, which
    /// ships unset — so an omitted value is <see cref="AgentKind.ClaudeCode"/> and nothing about an
    /// existing caller changes. Only <c>ClaudeCode</c>, <c>Grok</c>, and <c>Codex</c> are accepted
    /// (<see cref="Services.AgentTaskService.DelegatableKinds"/>), and only a Worker may be
    /// <c>Grok</c> or <c>Codex</c>.
    /// </summary>
    AgentKind? AgentKind = null,
    /// <summary>
    /// Null = let the server decide: workers run Shared; an orchestrator gets its own worktree
    /// unless it already has its own location. An explicit value is always honoured — with a
    /// warning when it puts an orchestrator in its caller's directory.
    /// </summary>
    WorkspaceMode? Workspace = null,
    /// <summary>Run somewhere else — another repo, another checkout. Null inherits the caller's.</summary>
    string? WorkingDirectory = null,
    string? Scope = null,
    string? MergeTargetRef = null,
    Guid? AgentId = null,
    /// <summary>
    /// Arm the PreToolUse deny hook in an orchestrator's worktree (blocks direct Edit/Write —
    /// "delegate this instead"). Null follows <c>Delegation:OrchestratorDenyHookEnabled</c>.
    /// </summary>
    bool? DenyDirectEdits = null,
    /// <summary>
    /// Follow-up: run this on the SAME agent that ran the given task (full guid or 8-char short
    /// id), keeping its context. The task inherits that agent's directory and tier; it waits in
    /// the queue while the agent is still busy. If that agent has retired (or the prior task never
    /// ran on one), creation degrades to a fresh delegate with the settled task's context prefixed
    /// to its goal.
    /// </summary>
    string? FollowUpOnTask = null,
    /// <summary>
    /// Roughly how long the caller expects this to take (1..1440 minutes). Null takes
    /// <c>Delegation:DefaultExpectedMinutes</c>. It schedules the first check-in and NOTHING else —
    /// it is a hint, never a deadline, and no code path fails or escalates a task for running past
    /// it (CARD-0047).
    /// </summary>
    int? ExpectedMinutes = null,
    /// <summary>
    /// Bypass the CARD-0136 subscription-quota launch gate. Default false: a fresh low
    /// reading refuses create with 409 <c>subscription_quota_low</c>. Re-send the same
    /// request with this true to queue anyway; the warning is recorded on the task.
    /// </summary>
    bool IgnoreSubscriptionQuota = false,
    /// <summary>
    /// Bypass the CARD-0309 create-time 409 <c>model_disabled</c>. Default false. True
    /// queues the task anyway; the dispatcher still skips it until the hold clears.
    /// Start never honours this flag — there is no spawn-into-a-held-model switch.
    /// </summary>
    bool IgnoreModelDisabled = false,
    /// <summary>
    /// Overlay applied when this task's process is launched (CARD-0106). Persisted on the
    /// task row so async dispatch and a task-session relaunch re-apply it. ANTIPHON_* names
    /// are refused 422. A non-empty overlay excludes the task from warm-pool reuse (reuse
    /// launches no process, so the overlay could never apply). Combined with
    /// <see cref="FollowUpOnTask"/> is refused 422 — a follow-up continues an existing
    /// process. Does not cascade to child tasks; blanket a subtree with a project default.
    /// </summary>
    IReadOnlyDictionary<string, string>? LaunchEnvOverride = null,
    /// <summary>
    /// LLM-routing values visible only in the caller's live process environment (CARD-0263 S3).
    /// The server keeps only configured inheritance names and prefers this snapshot over its
    /// reconstruction from the caller's stored Antiphon layers. This is not a launch override:
    /// it keeps the inherited merge position and warm-pool semantics.
    /// </summary>
    IReadOnlyDictionary<string, string>? InheritedLlmEnv = null,
    /// <summary>
    /// The card this work is against (CARD-0040), as a guid or any identifier shape
    /// <c>card.ps1</c> accepts (<c>CARD-0040</c>, <c>card-40</c>, <c>#40</c>, <c>40</c>). Omitted,
    /// the binding is derived: the parent / followed-up task's card, else the FIRST
    /// <c>CARD-nnnn</c> in the title. An EXPLICIT value that resolves to no card is a 422 — a
    /// binding the caller asked for and silently did not get is worse than none.
    /// </summary>
    string? Card = null,
    /// <summary>
    /// Run this task ON an existing STANDING agent (CARD-0291), referenced the way a caller types
    /// it: a guid, an exact slug, or a case-insensitive exact name. Neither name nor slug is unique
    /// in the schema, so a reference that binds nothing or more than one agent is a 422 naming the
    /// candidates — an explicit value that silently binds the wrong thing is worse than a refusal
    /// (same argument as <see cref="Card"/>). A pool delegate is refused too (that population is
    /// <see cref="FollowUpOnTask"/>'s job), as is a disagreeing <see cref="AgentId"/> or combining
    /// with <see cref="FollowUpOnTask"/>. The resolved id feeds the CARD-0140 pin path unchanged:
    /// kind is inherited, the task queues while the agent is busy, and the caller gets the normal
    /// completion note when it settles.
    /// </summary>
    string? Agent = null,
    /// <summary>
    /// Ignore the CARD-0305 routing pin for THIS create. Default false: a request that disagrees
    /// with a <c>Required</c> pin is refused 409 <c>routing_pin_conflict</c>. True proceeds with
    /// what the caller asked for and leaves the pin standing — it is one-shot, never a clear.
    /// Entirely distinct from <see cref="IgnoreModelDisabled"/>: this one is about which model
    /// SHOULD run this card+role, that one about whether the model MAY run at all.
    /// </summary>
    bool IgnoreRoutingPin = false,
    /// <summary>
    /// Caller-declared hardness (CARD-0090). When set, kind/level are chosen by walking the
    /// matching complexity chain. Combined with an explicit <see cref="AgentKind"/> or
    /// <see cref="ModelLevel"/> is 422 — an explicit pair is never silently rerouted.
    /// Combined with <see cref="IgnoreModelDisabled"/> is 422: a chain skips a held candidate.
    /// </summary>
    TaskComplexity? Complexity = null,
    /// <summary>
    /// When the chain is exhausted, 409 <c>routing_exhausted</c> instead of inserting a
    /// Blocked task (CARD-0090). Default false: Blocked-for-a-human is the durable decision
    /// request so an orchestrator cannot guess a kind.
    /// </summary>
    bool RefuseIfExhausted = false,
    /// <summary>
    /// Bypass the CARD-0324 create-time 409 <c>provider_sign_in_required</c>. Default false:
    /// a registry-Grok create whose <c>GROK_HOME</c> has no usable session is refused.
    /// True queues the task; the dispatcher still fails it if the store is empty at launch.
    /// For the operator who is about to run <c>grok login</c> and wants the task queued.
    /// </summary>
    bool AllowUnauthenticatedProvider = false,
    /// <summary>
    /// CARD-0294 S1. The caller's own words for what this task is already authorised to do.
    /// Trimmed, at most 2 000 characters. Injected into the child's brief so it does not stop
    /// to ask for a go-ahead this already grants, and replayed by <c>POST …/continue</c>.
    /// </summary>
    string? Authority = null,
    /// <summary>
    /// CARD-0294 S3's switch, accepted at create so the column is filled. Requires
    /// <see cref="Authority"/>. The fire-once auto-continue is a follow-on; a 422
    /// <c>auto_continue_needs_authority</c> refuses the flag without authority.
    /// </summary>
    bool AutoContinue = false);

public sealed record AgentTaskSummaryDto(
    Guid Id,
    Guid RootTaskId,
    Guid? ParentTaskId,
    int Depth,
    string Title,
    AgentTaskKind Kind,
    AgentTaskRole Role,
    /// <summary>Which agent program ran (or will run) it — ClaudeCode unless the caller chose Grok.</summary>
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    AgentModelLevel? EscalatedFrom,
    AgentTaskStatus Status,
    WorkspaceMode Workspace,
    string WorkingDirectory,
    string? RepoPath,
    /// <summary>Where a Worktree task actually runs — the throwaway checkout, branch included.</summary>
    string? WorktreePath,
    string? WorktreeBranch,
    string? Scope,
    /// <summary>Areas the task actually touched, filled at settlement (CARD-0063 S4).</summary>
    string? ObservedScope,
    Guid? AgentId,
    /// <summary>The delegate that ran (or is running) the work — the board chip names it.</summary>
    string? AgentName,
    Guid? AgentSessionId,
    int Attempt,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    DateTime? CompletedAt,
    DateTime? ReadAt,
    string? DeliverablePath,
    string? DeliverableRef,
    /// <summary>Non-null when an unbound-session recovery, rather than an observed finish, settled the task.</summary>
    DateTime? RecoveredAt,
    /// <summary>UNCACHED input only — add the two cache counters for a human "tokens in".</summary>
    long TokensIn,
    /// <summary>Cached prefix re-read per turn, priced at ~0.1x input. Dominates an agentic session.</summary>
    long CacheReadTokens,
    long CacheCreationTokens,
    long TokensOut,
    decimal CostUsd,
    /// <summary>
    /// 0 means the figure predates the CARD-0023 pricing fix (cache reads billed as fresh input,
    /// stale rates) and is roughly 10x high. The UI labels those rather than passing them off as
    /// current — the per-root ceiling still sums them.
    /// </summary>
    int CostPricingVersion,
    /// <summary>Rolled-up spend for this task and everything under it — what the board chip shows.</summary>
    decimal SubtreeCostUsd,
    int ChildCount,
    /// <summary>
    /// The caller's declared duration hint in minutes (CARD-0047). Never a deadline — a task past
    /// it is not late, it is just past the point where the first check-in was scheduled.
    /// </summary>
    int ExpectedDurationMinutes,
    /// <summary>When the next scheduled check-in is due; null means this task is never checked.</summary>
    DateTime? NextCheckAt,
    int CheckCount,
    /// <summary>A land is wanted and has not reached an outcome (CARD-0331). Null when none is pending.</summary>
    DateTime? LandRequestedAt = null,
    /// <summary>When the current land attempt started git work. Null while queued or held.</summary>
    DateTime? LandStartedAt = null,
    /// <summary>How many times this land request started git work.</summary>
    int LandAttempt = 0,
    /// <summary>The card this task's work is against (CARD-0040); null when nothing bound.</summary>
    Guid? CardId = null,
    /// <summary>Denormalised at read time so a row can name its card without a second request.</summary>
    string? CardIdentifier = null,
    /// <summary>
    /// How the stored report was classified at settlement (CARD-0159). <c>Legacy</c> on every
    /// pre-existing row; a new settlement is <c>Marked</c> / <c>UnmarkedAfterNudge</c> /
    /// <c>QuestionHeuristic</c> / <c>FinalMessageMissing</c> / <c>Exempt</c>.
    /// </summary>
    AgentTaskReportEvidence ReportEvidence = AgentTaskReportEvidence.Legacy,
    /// <summary>
    /// CARD-0090. Non-null when this task's kind/level was chosen by a complexity chain.
    /// </summary>
    TaskComplexity? Complexity = null,
    /// <summary>
    /// When the caller last answered this task (CARD-0348). Null until the first reply.
    /// Trailing so no positional constructor call moves.
    /// </summary>
    DateTime? RepliedAt = null);

/// <summary>Fleet-wide counters for the delegations board, independent of its history window.</summary>
public sealed record AgentTaskListSummaryDto(
    int Active,
    int Blocked,
    int Runs,
    decimal TotalCostUsd,
    IReadOnlyDictionary<string, int> ByStatus);

public sealed record AgentTaskDetailDto(
    AgentTaskSummaryDto Summary,
    string Goal,
    /// <summary>The delegate's final message, UNTOUCHED — forwarding may excerpt, this never does.</summary>
    string? Result,
    string? ResultFilePath,
    string? DeliverablePath,
    string? DeliverableRef,
    string? FailureReason,
    string? MergeTargetRef,
    IReadOnlyList<AgentTaskEventDto> Events,
    /// <summary>
    /// Machine-readable class of <see cref="FailureReason"/> when one was assigned (CARD-0256).
    /// Null on legacy and otherwise-unclassified failures.
    /// </summary>
    AgentTaskFailureCode? FailureCode = null,
    /// <summary>
    /// CARD-0033. Non-null iff <see cref="AgentTaskSummaryDto.Status"/> is Blocked: the isolated
    /// question, prior Q&amp;A rounds, and a bounded git progress snapshot. Null on every other
    /// status so non-Blocked drawers are unchanged.
    /// </summary>
    BlockedContextDto? Blocked = null,
    /// <summary>CARD-0294 S1. The caller's standing authority, or null when none was given.</summary>
    string? StandingAuthority = null,
    /// <summary>CARD-0294 S3's stored switch. The fire-once auto-continue is a follow-on.</summary>
    bool AutoContinueOnWait = false);

/// <summary>Why a task is Blocked — CARD-0033. RoutingExhausted is CARD-0090, added after the original three.</summary>
public enum BlockedKind
{
    Question = 0,
    MergeConflict = 1,
    CostCeiling = 2,
    RoutingExhausted = 3,
}

/// <summary>Where an answer to a blocked delegate was typed (CARD-0033). Recorded on the Replied event.</summary>
public enum AnswerOrigin
{
    Web = 0,
    Cli = 1,
    Channel = 2,
    /// <summary>CARD-0294 S3: the harness replayed standing authority. Appended after Channel; do not renumber.</summary>
    Authority = 3,
}

/// <summary>Projection of a Blocked task: the question, isolated from the report that preceded it.</summary>
public sealed record BlockedContextDto(
    BlockedKind Kind,
    int Round,
    DateTime BlockedAt,
    string Question,
    string? Context,
    IReadOnlyList<BlockedRoundDto> PriorRounds,
    BlockedProgressDto? Progress,
    bool CanAnswer,
    string? CannotAnswerReason,
    Guid? MergeTaskId,
    /// <summary>CARD-0294 S2. Vocabulary word: <c>marked-blocked</c> / <c>question-line</c> / <c>waiting-unmarked</c>. Null on non-question blocks.</summary>
    string? Reason = null,
    /// <summary>CARD-0294 S1. The standing authority given at dispatch, or null.</summary>
    string? Authority = null,
    /// <summary>CARD-0294 S1. True when this is a question the parent can answer with <c>-Continue</c>.</summary>
    bool CanContinue = false,
    /// <summary>CARD-0294 S3. When auto-continue already fired, so a later Block shows it was used.</summary>
    DateTime? AutoContinuedAt = null);

/// <summary>POST /api/agent-tasks/{id}/continue (CARD-0294 S1).</summary>
public sealed record ContinueAgentTaskRequest(AnswerOrigin? Origin = null);

public sealed record BlockedRoundDto(
    int Round,
    string Question,
    DateTime AskedAt,
    string? Answer,
    DateTime? AnsweredAt,
    AnswerOrigin? AnsweredVia);

public sealed record BlockedProgressDto(
    string? Branch,
    IReadOnlyList<string> Commits,
    int ChangedFiles,
    int UntrackedFiles,
    string? LastCheckDigest,
    DateTime? LastCheckAt,
    string? Unavailable);

/// <summary>POST /api/agent-tasks/{id}/reroute (CARD-0090).</summary>
public sealed record RerouteAgentTaskRequest(AgentKind AgentKind, AgentModelLevel ModelLevel);

public sealed record AgentTaskEventDto(
    AgentTaskEventType Type,
    AgentModelLevel? ModelLevel,
    string Detail,
    DateTime At);

/// <summary>
/// What the delegate script gets back — enough to print, not enough to poll with. The warning is
/// shown to the CALLER at the moment of creation (an orchestrator sharing its caller's directory,
/// a directory that can't be isolated) — the timeline records it too, but nobody reads a timeline
/// before the collision happens.
/// </summary>
public sealed record AgentTaskCreatedDto(
    Guid Id, string ShortId, AgentTaskStatus Status, AgentModelLevel ModelLevel, string? Warning = null,
    /// <summary>The resolved agent kind — the script echoes it so a Grok delegate is never a surprise.</summary>
    AgentKind AgentKind = Domain.Enums.AgentKind.ClaudeCode,
    /// <summary>
    /// True when this task's report will NOT be routed back to anybody — <c>ReplyTo == None</c>,
    /// which is every token-less caller (CARD-0020 S1): no token means no parent task and no parent
    /// session, so there is nowhere for the completion note to go and the result only ever lands on
    /// the board. That was previously stated ONLY in a comment on the endpoint, so a shell caller
    /// pasting a <c>curl</c> learned it by never receiving the report. Said here, at creation, it
    /// is the one moment the caller can still decide to send a token instead.
    /// </summary>
    bool NoReplyRouting = false,
    /// <summary>
    /// Tasks already running in this repo whose areas this one touches (CARD-0063 S3). The
    /// ergonomic centre of the card: the caller declares intent and is told, at once, what that
    /// intent costs — a wait, or a rebase to expect. Empty when nothing overlaps.
    /// </summary>
    IReadOnlyList<ScopeOverlapDto>? ScopeOverlaps = null,
    /// <summary>The card this task bound to (CARD-0040), or null. Printed at dispatch so a
    /// mis-binding is caught by the caller who can still fix it, not on the board a week later.</summary>
    Guid? CardId = null,
    /// <summary>The bound card's identifier — what <c>delegate.ps1</c> echoes as "bound to CARD-nnnn".</summary>
    string? CardIdentifier = null,
    /// <summary>
    /// How a requested follow-up was dispatched: either on its still-live agent, or as a fresh
    /// delegate after that agent was unavailable. Null when this was not a follow-up.
    /// </summary>
    string? FollowUpMessage = null,
    /// <summary>CARD-0090. The complexity tier that chose kind/level, when one did.</summary>
    TaskComplexity? Complexity = null,
    /// <summary>CARD-0090. The walk that produced kind/level (or exhausted the chain).</summary>
    ComplexityRoutingDto? Routing = null,
    /// <summary>
    /// CARD-0352 S3. True when create stored the Goal-first-line fallback and handed the
    /// task to antiphon-diagnose to replace the title. <c>delegate.ps1</c> prints
    /// <c>title: pending</c>. Creation never waits on the seat.
    /// </summary>
    bool TitleDiagnosisQueued = false);

/// <summary>
/// One running task a newly created task overlaps, and what the dispatcher will do about it.
/// </summary>
/// <param name="Policy"><c>serialise</c> (this task will wait) or <c>warn</c> (it will start anyway).</param>
/// <param name="Areas">The area names or path tokens that intersected; null when the overlap is
/// D3's "two shared writers in one checkout", which needs no declared scope.</param>
public sealed record ScopeOverlapDto(
    Guid TaskId,
    string ShortId,
    string Title,
    WorkspaceMode Workspace,
    string? Branch,
    string Policy,
    string? Areas);

public sealed record ReplyToAgentTaskRequest(
    string Message,
    int? Round = null,
    AnswerOrigin? Origin = null);

/// <summary>Optional narrow test filter for an explicit <c>POST /land</c> verification.</summary>
public sealed record LandAgentTaskRequest(string? Verify = null);

/// <summary>Manual tier bump. Null takes the next rung up (or the role policy's target).</summary>
public sealed record EscalateAgentTaskRequest(AgentModelLevel? ModelLevel = null);

/// <summary>
/// One named area of a repo, as the areas endpoint reports it (CARD-0063 S2).
/// </summary>
/// <param name="Name">The name a task's <c>scope</c> may use.</param>
/// <param name="Paths">The path globs it owns, as written in the file.</param>
/// <param name="Weight"><c>serialise</c> (the default) or <c>allow</c>.</param>
public sealed record AreaDto(string Name, IReadOnlyList<string> Paths, string Weight);

/// <summary>
/// A repo's declared areas. An empty list is the honest answer for a repo with no
/// <c>antiphon.areas.json</c> — every scope token is then read as a path or an opaque label.
/// </summary>
public sealed record AreaMapDto(string RepoPath, string? SourcePath, IReadOnlyList<AreaDto> Areas);

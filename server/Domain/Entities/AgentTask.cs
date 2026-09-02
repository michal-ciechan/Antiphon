using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One delegated unit of work. Deliberately NOT a <see cref="Card"/>: cards carry board columns,
/// tracker sync, workflow definitions and a 1:1 worktree, which is far too much for "run the test
/// suite". Tasks are cheap, nest, and can be created in bulk. A task MAY reference a card, but
/// doesn't need one.
///
/// Two shapes only (<see cref="Kind"/>): a Worker does a piece of work and reports; an Orchestrator
/// owns a chunk and runs its own agents. Nothing decomposes work automatically.
/// </summary>
public class AgentTask
{
    public Guid Id { get; set; }

    /// <summary>Equals <see cref="Id"/> for roots. Denormalised so a whole run is one query.</summary>
    public Guid RootTaskId { get; set; }

    public Guid? ParentTaskId { get; set; }

    /// <summary>The session the report is delivered into when <see cref="ReplyTo"/> is Session.</summary>
    public Guid? ParentSessionId { get; set; }

    /// <summary>0 for a root. Guards runaway nesting alongside the per-root cost ceiling.</summary>
    public int Depth { get; set; }

    /// <summary>One line — the board chip.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The caller's goal, verbatim. The reporting contract is composed around it at dispatch.</summary>
    public string Goal { get; set; } = string.Empty;

    public AgentTaskKind Kind { get; set; } = AgentTaskKind.Worker;
    public AgentTaskRole Role { get; set; } = AgentTaskRole.Custom;

    /// <summary>
    /// The project on whose behalf this task runs — the scope its <c>{{key:NAME}}</c>
    /// placeholders resolve against. Set once at creation from caller provenance (the parent task,
    /// or the calling session's card/board binding), never from a filesystem path: sibling
    /// worktrees make path matching unsafe. Null means no trustworthy project identity and thus
    /// global-only key resolution (CARD-0115 S1).
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Launch-time env overlay for this task (CARD-0106). JSON object, default <c>{}</c>.
    /// Re-applied on dispatch and on a task-session relaunch; a non-empty overlay excludes
    /// the task from warm-pool reuse because reuse launches no process.
    /// </summary>
    public string LaunchEnvOverrideJson { get; set; } = "{}";

    /// <summary>
    /// Caller-sourced LLM-routing env snapshot, taken at create (CARD-0260 S1). JSON object,
    /// default <c>{}</c>. Filtered to <c>Delegation:LlmEnvInheritance:Names</c>. Merged after
    /// the project default and before this task's agent env, so an explicit override and the
    /// child agent's own <c>LaunchEnvJson</c> still win. Follow-ups and standing-agent pins
    /// leave this empty — those continue an existing process.
    /// </summary>
    public string InheritedLaunchEnvJson { get; set; } = "{}";

    /// <summary>
    /// WHICH AGENT PROGRAM runs this task — a different axis from <see cref="Kind"/> (which is
    /// worker-vs-orchestrator). Defaults to <see cref="AgentKind.ClaudeCode"/>, which is what every
    /// row created before CARD-0084 carries, so nothing about an existing task changes.
    ///
    /// <para>Only <c>ClaudeCode</c> and <c>Grok</c> are delegatable today (CARD-0084 S2's
    /// allowlist), and only a Worker may be Grok: an orchestrator's contract — the deny hook,
    /// delegate.ps1 usage, the check interpreter — has only ever been exercised on Claude.</para>
    /// </summary>
    public AgentKind AgentKind { get; set; } = AgentKind.ClaudeCode;

    /// <summary>Resolved from the role policy at creation; an explicit override is recorded in the events.</summary>
    public AgentModelLevel ModelLevel { get; set; } = AgentModelLevel.High;

    /// <summary>
    /// Non-null = this task's kind/level was chosen by a complexity chain and may be re-chosen
    /// by it (dispatch re-walk, Blocked-for-routing resume). Null after an explicit
    /// <c>POST …/reroute</c> — that pick ends chain governance (CARD-0090).
    /// </summary>
    public TaskComplexity? Complexity { get; set; }

    /// <summary>Set when the task was escalated up a tier — the chip shows the ladder.</summary>
    public AgentModelLevel? EscalatedFrom { get; set; }

    public int Attempt { get; set; } = 1;
    public int MaxAttempts { get; set; } = 2;

    /// <summary>
    /// Workers default to Shared; an orchestrator defaults to its own worktree unless it already
    /// has its own location (see <see cref="WorkspaceMode"/>).
    /// </summary>
    public WorkspaceMode Workspace { get; set; } = WorkspaceMode.Shared;

    /// <summary>
    /// Arm the PreToolUse deny hook in this orchestrator's worktree at dispatch. Null follows
    /// <c>Delegation:OrchestratorDenyHookEnabled</c>; workers never get the hook.
    /// </summary>
    public bool? DenyDirectEdits { get; set; }

    /// <summary>
    /// Absolute directory the delegate runs in. A property of the TASK, not inherited from the
    /// parent — that is what makes cross-repo orchestration (an agent per repo) work. Validated
    /// against <c>Delegation.AllowedRoots</c> at creation.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Repo toplevel derived from the working directory; null when it isn't a git repo.</summary>
    public string? RepoPath { get; set; }

    /// <summary>
    /// The card this task's work is against (CARD-0040). The missing edge between delegation and
    /// the board: with it, "work started" and "work settled" are durable rows a sweep can move a
    /// card on, instead of a convention that only ever lived in the title's prose.
    ///
    /// <para>Set once at creation, in precedence order: an explicit <c>Card</c> on the request; the
    /// parent / followed-up / conflicted task's binding; the FIRST <c>CARD-nnnn</c> in the title.
    /// <see cref="AgentTaskRole.Check"/> rows are never bound - they are about a task, not a card.
    /// Null is normal and never an error: the task runs, the card simply does not move.</para>
    ///
    /// <para>The FK is <c>ON DELETE SET NULL</c>. Cards are archived rather than deleted, but a
    /// task must never become undeletable by <c>DataRetentionService</c> because of a card.</para>
    /// </summary>
    public Guid? CardId { get; set; }

    public Guid? WorktreeId { get; set; }

    /// <summary>
    /// Where a Worktree task actually runs — filled at dispatch by <c>git worktree add</c>. The
    /// card-scoped <see cref="Worktree"/> entity requires a card, which a task doesn't have, so the
    /// task carries its own worktree coordinates.
    /// </summary>
    public string? WorktreePath { get; set; }

    /// <summary>The task branch the worktree is on; what merges into <see cref="MergeTargetRef"/>.</summary>
    public string? WorktreeBranch { get; set; }

    /// <summary>Branch a Worktree task merges into. Defaults to the parent's branch; null leaves it for a human.</summary>
    public string? MergeTargetRef { get; set; }

    /// <summary>Advisory file lease — two Shared tasks with intersecting globs are serialised.</summary>
    public string? Scope { get; set; }

    /// <summary>
    /// What the task actually touched, mapped back onto the repo's areas at settlement
    /// (CARD-0063 S4). Same shape as <see cref="Scope"/>: area names, plus any path that matched
    /// no area. Observability only — nothing is ever failed, held or killed for drifting.
    /// </summary>
    public string? ObservedScope { get; set; }

    /// <summary>Pinned agent; null means an ephemeral one is spawned at the task's tier.</summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// The delegate's name, snapshotted at dispatch. Denormalised on purpose: an ephemeral agent's
    /// row is deleted when the task settles, and the board chip must keep naming who ran the work.
    /// </summary>
    public string? AgentName { get; set; }

    public Guid? AgentSessionId { get; set; }

    /// <summary>Throwaway agent — hidden from the agents page and removed when the task settles.</summary>
    public bool Ephemeral { get; set; } = true;

    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Queued;
    public AgentTaskReplyTo ReplyTo { get; set; } = AgentTaskReplyTo.None;

    /// <summary>
    /// The delegate's final assistant message, UNTOUCHED. Forwarding may excerpt it (§2.4 of the
    /// spec) but this always holds the original.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>Set when the delegate spilled a long report to a file — the report references it.</summary>
    public string? ResultFilePath { get; set; }

    /// <summary>The first repository markdown deliverable named by the immutable final report.</summary>
    public string? DeliverablePath { get; set; }

    /// <summary>The branch holding <see cref="DeliverablePath"/> when it was not on disk at settlement.</summary>
    public string? DeliverableRef { get; set; }

    /// <summary>When an operator first opened this task's deliverable or report.</summary>
    public DateTime? ReadAt { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>
    /// Machine-readable class of <see cref="FailureReason"/> when one was assigned (CARD-0256).
    /// Null on every legacy failure and on failures that still only have prose. The create/retry
    /// repeat guard keys on this, never on parsing <see cref="FailureReason"/>.
    /// </summary>
    public AgentTaskFailureCode? FailureCode { get; set; }

    /// <summary>The normalized digest of the settled report last read by this task's parent session.</summary>
    public string? LastPolledResultHash { get; set; }

    /// <summary>When the parent session last read <see cref="LastPolledResultHash"/> through status polling.</summary>
    public DateTime? LastPolledResultAt { get; set; }

    /// <summary>Guards against two dispatcher ticks claiming the same task.</summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    /// <summary>Hashed bearer the delegate's session presents when calling back. Never stored raw.</summary>
    public string? TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// When an unbound-session recovery settled this task. Unlike <see cref="CompletedAt"/>, this
    /// is not an observed delegate completion, so duration consumers can label or exclude it.
    /// </summary>
    public DateTime? RecoveredAt { get; set; }

    /// <summary>
    /// Roughly how long the caller thinks this will take, in minutes (CARD-0047). Resolved at
    /// creation from the request or <c>Delegation:DefaultExpectedMinutes</c>, so it is always a
    /// number and the scheduler never has to reason about "unset".
    ///
    /// <para>It is a HINT, NEVER A DEADLINE. Nothing fails, escalates, kills or reprioritises a
    /// task because it ran past this — the existing stall and delivery clocks
    /// (<c>AutoEscalateStalledAsync</c>, <c>FailNeverStartedAsync</c>) are independent and are not
    /// fed by it. All it does is decide when the FIRST check-in happens.</para>
    /// </summary>
    public int ExpectedDurationMinutes { get; set; } = 10;

    /// <summary>
    /// When the next scheduled check-in on this delegate is due, or null for "never check".
    /// Armed at dispatch only when <see cref="ReplyTo"/> is Session — a check with nobody to
    /// deliver to is dead weight — and advanced (re-armed BEFORE the check runs, so a crash
    /// mid-check skips one check instead of looping) by the dispatcher's check sweep.
    /// </summary>
    public DateTime? NextCheckAt { get; set; }

    /// <summary>How many check-ins have been claimed for this task; drives the backoff and the cap.</summary>
    public int CheckCount { get; set; }

    /// <summary>
    /// UNCACHED input tokens only. The three input counters are kept apart because they are priced
    /// apart — a cache read is ~0.1x this, a cache write 1.25x (CARD-0023). Anything showing a
    /// human "tokens in" wants the sum of all three, not this alone.
    /// </summary>
    public long TokensIn { get; set; }

    /// <summary>Cached prefix re-read on a turn — the counter that dominates an agentic session.</summary>
    public long CacheReadTokens { get; set; }

    /// <summary>Tokens written into the prompt cache.</summary>
    public long CacheCreationTokens { get; set; }

    public long TokensOut { get; set; }
    public decimal CostUsd { get; set; }

    /// <summary>
    /// Which costing model produced <see cref="CostUsd"/> (<see cref="Services.DelegationCost.PricingVersion"/>).
    /// 0 means the row was priced before CARD-0023 — cache reads billed as fresh input against a
    /// stale rate table, so roughly an order of magnitude high. The per-root ceiling still sums
    /// those rows, so they are labelled in the API and UI rather than silently trusted.
    /// </summary>
    public int CostPricingVersion { get; set; }

    /// <summary>
    /// How the stored report was classified at settlement (CARD-0159). Default
    /// <see cref="AgentTaskReportEvidence.Legacy"/> so every pre-existing row is labelled rather
    /// than guessed; a new settlement always writes a non-Legacy value.
    /// </summary>
    public AgentTaskReportEvidence ReportEvidence { get; set; }

    /// <summary>
    /// When the one "please send the closing report line" nudge was queued (CARD-0159). Null means
    /// not yet asked; a second unmarked <c>end_turn</c> after the delivered nudge settles rather
    /// than nudging again (CARD-0248).
    /// </summary>
    public DateTime? ReportNudgedAt { get; set; }

    /// <summary>
    /// Transcript Sequence of the TurnEnd boundary the one nudge (CARD-0159) was issued against
    /// (CARD-0248). Settle-anyway requires the current boundary to be LATER than this one — the
    /// contract is "asked once and it ended ANOTHER turn unmarked", and before this column the
    /// same boundary re-entering through the 5 s sweep satisfied it.
    /// </summary>
    public long? ReportNudgedSequence { get; set; }

    /// <summary>
    /// The SessionQueuedMessages row carrying the nudge (CARD-0248). Settle-anyway also requires
    /// that row's SentAt to be non-null: a WhenIdle nudge can sit queued for many minutes while
    /// the delegate is genuinely mid-turn, and settling before it is typed answers a question
    /// that was never asked.
    /// </summary>
    public Guid? ReportNudgeMessageId { get; set; }

    /// <summary>
    /// HEAD SHA of the task worktree at creation (CARD-0159 S3). The no-merge-target base for
    /// <c>git=N commits, M files</c> on the completion header, and the same base
    /// <c>DelegateCheckProbe</c> uses so the check digest and the header agree. Null on legacy
    /// rows → <c>git=base unknown</c>.
    /// </summary>
    public string? WorktreeBaseSha { get; set; }

    /// <summary>
    /// CARD-0299 S2. How many times a cold Codex first-delivery <c>NoSubmitOutput</c> has
    /// already killed-and-relaunched this task. Default 0. Compared to
    /// <c>DelegationSettings.BootWedgeRelaunchLimit</c> (1).
    /// </summary>
    public int BootWedgeRelaunchCount { get; set; }

    public Card? Card { get; set; }
    public AgentTask? ParentTask { get; set; }
    public ICollection<AgentTask> Children { get; set; } = new List<AgentTask>();
    public ICollection<AgentTaskEvent> Events { get; set; } = new List<AgentTaskEvent>();
}

/// <summary>
/// Append-only timeline for a task — dispatched, escalated, merged, conflicted, retried. Gives the
/// board drawer its history and mirrors the existing <c>AuditRecord</c> habit.
/// </summary>
public class AgentTaskEvent
{
    public Guid Id { get; set; }
    public Guid AgentTaskId { get; set; }
    public AgentTaskEventType Type { get; set; }
    public AgentModelLevel? ModelLevel { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime At { get; set; }

    public AgentTask AgentTask { get; set; } = null!;
}

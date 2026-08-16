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

    /// <summary>Resolved from the role policy at creation; an explicit override is recorded in the events.</summary>
    public AgentModelLevel ModelLevel { get; set; } = AgentModelLevel.High;

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
    public string? ScopeGlob { get; set; }

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

    public string? FailureReason { get; set; }

    /// <summary>Guards against two dispatcher ticks claiming the same task.</summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    /// <summary>Hashed bearer the delegate's session presents when calling back. Never stored raw.</summary>
    public string? TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

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

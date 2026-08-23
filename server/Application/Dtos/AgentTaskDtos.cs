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
    /// existing caller changes. Only <c>ClaudeCode</c> and <c>Grok</c> are accepted, and only a
    /// Worker may be <c>Grok</c>.
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
    string? ScopeGlob = null,
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
    /// the queue while the agent is still busy.
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
    /// Overlay applied when this task's process is launched (CARD-0106). Persisted on the
    /// task row so async dispatch and a task-session relaunch re-apply it. ANTIPHON_* names
    /// are refused 422. A non-empty overlay excludes the task from warm-pool reuse (reuse
    /// launches no process, so the overlay could never apply). Combined with
    /// <see cref="FollowUpOnTask"/> is refused 422 — a follow-up continues an existing
    /// process. Does not cascade to child tasks; blanket a subtree with a project default.
    /// </summary>
    IReadOnlyDictionary<string, string>? LaunchEnvOverride = null);

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
    string? ScopeGlob,
    Guid? AgentId,
    /// <summary>The delegate that ran (or is running) the work — the board chip names it.</summary>
    string? AgentName,
    Guid? AgentSessionId,
    int Attempt,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    DateTime? CompletedAt,
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
    int CheckCount);

public sealed record AgentTaskDetailDto(
    AgentTaskSummaryDto Summary,
    string Goal,
    /// <summary>The delegate's final message, UNTOUCHED — forwarding may excerpt, this never does.</summary>
    string? Result,
    string? ResultFilePath,
    string? FailureReason,
    string? MergeTargetRef,
    IReadOnlyList<AgentTaskEventDto> Events);

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
    bool NoReplyRouting = false);

public sealed record ReplyToAgentTaskRequest(string Message);

/// <summary>Manual tier bump. Null takes the next rung up (or the role policy's target).</summary>
public sealed record EscalateAgentTaskRequest(AgentModelLevel? ModelLevel = null);

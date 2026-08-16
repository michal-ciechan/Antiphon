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
    int? ExpectedMinutes = null);

public sealed record AgentTaskSummaryDto(
    Guid Id,
    Guid RootTaskId,
    Guid? ParentTaskId,
    int Depth,
    string Title,
    AgentTaskKind Kind,
    AgentTaskRole Role,
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
    Guid Id, string ShortId, AgentTaskStatus Status, AgentModelLevel ModelLevel, string? Warning = null);

public sealed record ReplyToAgentTaskRequest(string Message);

/// <summary>Manual tier bump. Null takes the next rung up (or the role policy's target).</summary>
public sealed record EscalateAgentTaskRequest(AgentModelLevel? ModelLevel = null);

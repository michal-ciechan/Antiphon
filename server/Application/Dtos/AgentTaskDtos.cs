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
    string? FollowUpOnTask = null);

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
    string? ScopeGlob,
    Guid? AgentId,
    /// <summary>The delegate that ran (or is running) the work — the board chip names it.</summary>
    string? AgentName,
    Guid? AgentSessionId,
    int Attempt,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    DateTime? CompletedAt,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    /// <summary>Rolled-up spend for this task and everything under it — what the board chip shows.</summary>
    decimal SubtreeCostUsd,
    int ChildCount);

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

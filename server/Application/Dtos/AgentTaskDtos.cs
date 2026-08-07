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
    /// <summary>Shared unless the caller opts into isolation.</summary>
    WorkspaceMode Workspace = WorkspaceMode.Shared,
    /// <summary>Run somewhere else — another repo, another checkout. Null inherits the caller's.</summary>
    string? WorkingDirectory = null,
    string? ScopeGlob = null,
    string? MergeTargetRef = null,
    Guid? AgentId = null);

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

/// <summary>What the delegate script gets back — enough to print, not enough to poll with.</summary>
public sealed record AgentTaskCreatedDto(Guid Id, string ShortId, AgentTaskStatus Status, AgentModelLevel ModelLevel);

public sealed record ReplyToAgentTaskRequest(string Message);

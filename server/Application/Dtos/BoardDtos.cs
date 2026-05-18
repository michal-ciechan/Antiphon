using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record BoardSummaryDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    string Description,
    TrackerKind TrackerKind,
    int MaxConcurrentSessions,
    int CardCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record BoardDetailDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    string Description,
    TrackerKind TrackerKind,
    int MaxConcurrentSessions,
    IReadOnlyList<BoardColumnDto> Columns,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record BoardColumnDto(
    Guid Id,
    string StateKey,
    string Name,
    int ColumnOrder,
    CardStatus CardStatus,
    bool IsActive,
    bool IsTerminal,
    int? MaxConcurrentSessions,
    IReadOnlyList<CardDto> Cards);

public sealed record CardDto(
    Guid Id,
    Guid BoardId,
    Guid BoardColumnId,
    Guid? OwnerSessionId,
    Guid? CurrentWorktreeId,
    Guid? AssignedAgentId,
    string? AssignedAgentName,
    int? AgentQueuePosition,
    Guid? ActiveWorkflowRunId,
    CardWorkflowRunStatus? WorkflowRunStatus,
    string? CurrentWorkflowStageName,
    string Identifier,
    string Title,
    string Description,
    int Priority,
    IReadOnlyList<string> Labels,
    CardStatus Status,
    Guid ConcurrencyToken,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? TerminalReason,
    IReadOnlyList<AgentSessionSummaryDto> Sessions);

public sealed record AgentSessionSummaryDto(
    Guid Id,
    string DefinitionName,
    AgentKind AgentKind,
    SessionStatus Status,
    string Cwd,
    DateTime CreatedAt,
    DateTime StartedAt,
    DateTime LastSeenAt,
    DateTime? EndedAt,
    int? ExitCode,
    string? FailureReason);

public sealed record CreateBoardRequest(
    Guid ProjectId,
    string Name,
    string? Description = null,
    int MaxConcurrentSessions = 1);

public sealed record CreateCardRequest(
    Guid? BoardColumnId,
    string Title,
    string? Description = null,
    int Priority = 0,
    IReadOnlyList<string>? Labels = null);

public sealed record MoveCardRequest(Guid BoardColumnId, Guid ConcurrencyToken);

public sealed record SpawnCardRequest(
    string? DefinitionName = null,
    int Cols = 120,
    int Rows = 30,
    string? Prompt = null,
    Guid? ConcurrencyToken = null);

public sealed record SpawnCardResult(Guid CardId, Guid SessionId);

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

/// <param name="Reason">
/// Why this card is moving. Optional, and deliberately NOT named for the close case: "no longer
/// wanted" and "fixed as part of CARD-nnnn" are what motivated it, but "moved back because the
/// spec changed" or "started early to unblock CARD-nnnn" are the same kind of fact, and a field
/// named for one use is how a second one ends up as a second field.
///
/// <para>Currently it PERSISTS only on a move into a terminal column, where it becomes
/// <c>Card.TerminalReason</c>. On any other move it is accepted and then DROPPED — there is no
/// per-card history to store it in yet. That arrives with CARD-0019's <c>CardRevision</c>, and
/// this is the second caller waiting for it. Callers should pass a reason regardless: the API
/// shape is right, the storage is what is missing, and a dropped reason is better than a caller
/// learning not to send one.</para>
/// </param>
public sealed record MoveCardRequest(
    Guid BoardColumnId, Guid ConcurrencyToken, string? Reason = null);

public sealed record SpawnCardRequest(
    string? DefinitionName = null,
    int Cols = 120,
    int Rows = 30,
    string? Prompt = null,
    Guid? ConcurrencyToken = null,
    // When set, the launched agent is renamed to this and put into remote-control mode
    // (via /rename + /remote-control) before the work prompt is sent.
    string? RemoteControlName = null);

public sealed record SpawnCardResult(Guid CardId, Guid SessionId);

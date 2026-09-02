using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>Which table a home-rail item was projected from (CARD-0002).</summary>
public enum HomeTaskSource
{
    Card = 0,
    Delegation = 1,
}

/// <summary>
/// The five home-rail groups, in display order. Numeric order IS display order; do not renumber.
/// </summary>
public enum HomeTaskGroup
{
    NeedsHuman = 0,
    Running = 1,
    Review = 2,
    Next = 3,
    Done = 4,
}

/// <summary>
/// Why a Needs-you or To-review item is there. Membership comes from record status, never
/// re-derived; the question text is read from <c>GET /api/attention</c>, not carried here.
/// </summary>
public enum HomeTaskHumanReason
{
    Decision = 0,
    Question = 1,
    Gate = 2,
    Review = 3,
}

/// <summary>
/// The delegation currently (or most recently) working a card. Null when none is bound.
/// </summary>
public sealed record HomeTaskWorkerDto(
    Guid TaskId,
    string ShortId,
    AgentTaskRole Role,
    AgentTaskStatus Status,
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    Guid? AgentId,
    string? AgentName,
    Guid? AgentSessionId,
    decimal CostUsd,
    DateTime? DispatchedAt,
    DateTime? CompletedAt);

/// <summary>
/// One home-rail item: a board card, or an unbound delegation. Bound tasks are never items —
/// they appear as <see cref="Worker"/> on their card.
/// </summary>
/// <param name="Key">
/// <c>card:{id:N}</c> or <c>task:{id:N}</c> — stable React key, and the tie-break sort key.
/// </param>
/// <param name="TerminalReason">Card close verdict (first-line read on the rail). Null for delegations and open cards.</param>
/// <param name="State">Native status name verbatim (<c>NeedsDecision</c>, <c>Working</c>), never remapped.</param>
/// <param name="Stage">
/// <c>ActiveWorkflowRun.CurrentStage.Name</c>, else the newest bound task's Role name (cards);
/// the task's own Role name (delegations). Null only for a card that has never had a bound task.
/// </param>
public sealed record HomeTaskItemDto(
    string Key,
    HomeTaskSource Source,
    Guid Id,
    string Identifier,
    string Title,
    string? TerminalReason,
    HomeTaskGroup Group,
    string State,
    HomeTaskHumanReason? HumanReason,
    string? Stage,
    CardWorkflowRunStatus? WorkflowRunStatus,
    CardImportance? Importance,
    CardUrgency? EffectiveUrgency,
    CardQuadrant? Quadrant,
    int? Rank,
    DateTime? UrgentSince,
    Guid? BoardId,
    HomeTaskWorkerDto? Worker,
    Guid? OwnerAgentId,
    AgentKind? AgentKind,
    AgentModelLevel? ModelLevel,
    AgentModelLevel? EscalatedFrom,
    AgentTaskRole? Role,
    decimal? CostUsd,
    Guid? AgentId,
    string? AgentName,
    Guid? AgentSessionId,
    DateTime? ReadAt,
    string? DeliverablePath,
    string? DeliverableRef,
    string? WorkingDirectory,
    string? RepoPath,
    string? WorktreePath,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public sealed record HomeTasksDto(DateTime GeneratedAt, IReadOnlyList<HomeTaskItemDto> Items);

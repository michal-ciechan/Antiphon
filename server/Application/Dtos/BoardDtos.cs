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
    IReadOnlyList<AgentSessionSummaryDto> Sessions,
    // How many revisions this card has, so a UI can show an "edited" affordance without a second
    // query. Non-zero on any card that has ever moved — moves are revisions too.
    int RevisionCount = 0,
    DateTime? ArchivedAt = null,
    string? ArchivedReason = null,
    string? ArchivedBy = null);

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
/// <para>It PERSISTS on every move, as the reason on the card's <c>Move</c> revision. A move into
/// a terminal column additionally becomes <c>Card.TerminalReason</c>, the cheap-to-read summary.
/// (Until CARD-0019 there was no per-card history and a non-terminal move's reason was accepted
/// and then dropped.)</para>
/// </param>
public sealed record MoveCardRequest(
    Guid BoardColumnId, Guid ConcurrencyToken, string? Reason = null);

/// <summary>
/// A correction to a card's text. Deliberately not an overload of <c>PATCH /cards/{id}</c>, which
/// is move-only: mixing "move" and "rewrite" into one verb invites partial-intent bugs.
/// </summary>
/// <param name="Reason">
/// REQUIRED. A correction that does not say why is how a record silently rots — the whole point of
/// the endpoint is that the surface can change without the history being lost.
/// </param>
/// <param name="Title">Null means unchanged. Same for every other content field.</param>
/// <param name="EditedBy">
/// Self-reported author (agent name, "operator"). The server has no principals, so this is honest
/// free text and must never be presented as an authenticated actor.
/// </param>
public sealed record UpdateCardContentRequest(
    Guid ConcurrencyToken,
    string Reason,
    string? Title = null,
    string? Description = null,
    int? Priority = null,
    IReadOnlyList<string>? Labels = null,
    string? EditedBy = null);

/// <summary>
/// Archive is what "delete" means for a card: the row stays, so references to its identifier never
/// dangle and the identifier allocator never hands the number out again.
/// </summary>
public sealed record ArchiveCardRequest(
    Guid ConcurrencyToken, string Reason, string? ArchivedBy = null);

/// <summary>Undoing an archive — same token/reason contract; mistakes need correcting too.</summary>
public sealed record UnarchiveCardRequest(
    Guid ConcurrencyToken, string Reason, string? UnarchivedBy = null);

/// <summary>
/// One entry of a card's immutable history, newest first. A <c>ContentEdit</c> carries the values
/// it SUPERSEDED (so entry n plus the current card is the whole history); a <c>Move</c> carries the
/// transition and no text; <c>Archive</c>/<c>Unarchive</c> carry only their reason.
/// </summary>
public sealed record CardRevisionDto(
    Guid Id,
    Guid CardId,
    int RevisionNumber,
    CardRevisionKind Kind,
    string? Title,
    string? Description,
    int? Priority,
    IReadOnlyList<string>? Labels,
    Guid? FromColumnId,
    Guid? ToColumnId,
    CardStatus? FromStatus,
    CardStatus? ToStatus,
    string? Reason,
    string? EditedBy,
    DateTime CreatedAt);

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

using System.Text.Json.Serialization;
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
    DateTime UpdatedAt,
    DateTime? ArchivedAt = null,
    string? ArchivedReason = null,
    string? ArchivedBy = null);

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
    DateTime UpdatedAt,
    DateTime? ArchivedAt = null,
    string? ArchivedReason = null,
    string? ArchivedBy = null);

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
    string? ArchivedBy = null,
    DateTime? AutoDispatchHeldAt = null,
    ExternalIssueDto? ExternalIssue = null,
    // Full card routes predate summary views. Omitting false preserves that wire contract byte for
    // byte; summary consumers deserialize an omitted value as false.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HasMore = false);

public sealed record CardListDto(IReadOnlyList<CardDto> Cards, bool Truncated);

public sealed record ExternalIssueDto(
    TrackerKind TrackerKind,
    string Key,
    string Url);

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
    string? FailureReason,
    Guid? TuiProfileRevisionId = null,
    string? EffectiveModelId = null,
    // Newest-usage-row fullness (tokens / ceiling). Null = unknown: no usage yet, or a
    // CompactBoundary / /clear landed after the last usage-bearing row (CARD-0082). Computed
    // on read; never persisted.
    double? ContextFullness = null,
    // Why ContextFullness is a number or null (CARD-0178). Additive; older servers omit it.
    ContextFullnessState? ContextFullnessState = null,
    // CARD-0180 S4 / CARD-0190: "bound" | "unbound" | "awaiting-input" when the runner
    // answered; null = unknown (older runner / unreachable / no tailer).
    string? TranscriptBinding = null,
    // CARD-0163: Herdr's screen-derived status, overlaid from the runner. Null means no Herdr
    // session or an older/unreachable runner; it is intentionally not guessed from transcript state.
    string? HerdrAgentStatus = null,
    DateTime? HerdrAgentStatusSinceUtc = null);

public sealed record CreateBoardRequest(
    Guid ProjectId,
    string Name,
    string? Description = null,
    int MaxConcurrentSessions = 1);

/// <summary>
/// Archive is what "delete" means for a board: the row stays, so cards and agents never dangle.
/// Boards have no concurrency token (unlike cards); the reason is the whole request.
/// </summary>
public sealed record ArchiveBoardRequest(string Reason, string? ArchivedBy = null);

/// <summary>Undoing a board archive — same reason contract; mistakes need correcting too.</summary>
public sealed record UnarchiveBoardRequest(string Reason, string? UnarchivedBy = null);

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
/// <param name="Spawn">
/// Whether a move into an ACTIVE column may start an agent session on the card. Defaults to FALSE,
/// which is a behaviour change: the move used to spawn unconditionally and say nothing about it,
/// so a scripted PATCH that only meant to file a card where it belongs would start work and the
/// caller would find out later, or never. The measured accidents were all scripted moves; the UI,
/// which asks first and warns in the dialog, passes true.
/// </param>
public sealed record MoveCardRequest(
    Guid BoardColumnId, Guid ConcurrencyToken, string? Reason = null, bool Spawn = false);

/// <summary>
/// What a move DID, not just what the card looks like afterwards.
/// </summary>
/// <param name="SpawnedSessionId">
/// The session this move started, or null. Previously <c>MoveAsync</c> called <c>SpawnAsync</c> and
/// threw the <c>SpawnCardResult</c> away, so the one caller who most needed to know a session had
/// been launched — a script — had no way to learn it.
/// </param>
/// <param name="SpawnSuppressed">
/// True when the target column was active and unowned and <c>Spawn</c> was false: the card moved
/// into a column where work happens and NO work started. Saying so is the point — the alternative
/// is a caller discovering a card sitting in In Progress with nobody on it, days later.
/// </param>
public sealed record MoveCardResult(
    CardDto Card, Guid? SpawnedSessionId, bool SpawnSuppressed);

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
/// Undo a terminal close. Dedicated verb, not a move: <c>Done</c>/<c>Canceled</c> stay
/// unreachable via <c>PATCH /cards/{id}</c>. Reopen never spawns — want an agent afterwards,
/// <c>POST /spawn</c>.
/// </summary>
public sealed record ReopenCardRequest(
    Guid ConcurrencyToken,
    string Reason,
    Guid? BoardColumnId = null,
    string? ReopenedBy = null);

/// <summary>
/// One entry of a card's immutable history, newest first. A <c>ContentEdit</c> carries the values
/// it SUPERSEDED (so entry n plus the current card is the whole history); a <c>Move</c> carries the
/// transition and no text; a <c>Reopen</c> carries the transition AND the superseded
/// <c>TerminalReason</c>/<c>CompletedAt</c>; <c>Archive</c>/<c>Unarchive</c> carry only their reason.
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
    DateTime CreatedAt,
    string? TerminalReason = null,
    DateTime? CompletedAt = null);

/// <summary>
/// What a card's text may weigh, in characters, straight from the <c>CardService</c> constants that
/// enforce it.
/// </summary>
/// <remarks>
/// The 422 side has been self-describing since CARD-0019 — an over-limit value comes back naming
/// both the limit and the actual length. What was missing is asking BEFORE composing: a caller
/// assembling a 20 KB description from a file has no way to find out that it will not fit except by
/// sending it. Every field here is the constant itself, so the endpoint cannot drift from the
/// enforcement; a test asserts that equality rather than the numbers.
/// </remarks>
public sealed record CardLimitsDto(
    int MaxTitleLength,
    int MaxDescriptionLength,
    int MaxReasonLength,
    int MaxActorLength);

public sealed record SpawnCardRequest(
    string? DefinitionName = null,
    int Cols = 120,
    int Rows = 30,
    string? Prompt = null,
    Guid? ConcurrencyToken = null,
    // When set, the launched agent is renamed to this and put into remote-control mode
    // (via /rename + /remote-control) before the work prompt is sent.
    string? RemoteControlName = null,
    /// <summary>
    /// Overlay on this spawn only (CARD-0106). Same contract as
    /// <c>StartAgentRequest.LaunchEnvOverride</c>: ephemeral, ANTIPHON_* refused 422,
    /// no cascade to child tasks.
    /// </summary>
    IReadOnlyDictionary<string, string>? LaunchEnvOverride = null);

public sealed record SpawnCardResult(Guid CardId, Guid SessionId);

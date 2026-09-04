using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>GET /api/stage-outcomes (CARD-0272). Rows plus the hit-rate summary over the same set.</summary>
public sealed record StageOutcomeListDto(
    IReadOnlyList<StageOutcomeDto> Rows,
    IReadOnlyList<StageOutcomeSummaryRowDto> Summary);

public sealed record StageOutcomeDto(
    Guid Id,
    OrchestrationStage Stage,
    StageOutcomeKind Outcome,
    StageOutcomeSource Source,
    Guid? SubjectTaskId,
    Guid? StageTaskId,
    Guid? CardId,
    decimal? CostUsd,
    long? TokensIn,
    long? TokensOut,
    int DurationSeconds,
    Guid? ResolutionTaskId,
    decimal? ResolutionCostUsd,
    string Detail,
    string? Ref,
    Guid? SupersedesId,
    DateTime RecordedAt);

/// <summary>
/// One stage's counts over the filtered (and optionally latest-per-task) rows.
/// <see cref="HitPercent"/> is Found / (Found + Clean); null when that denominator is zero.
/// <see cref="UsdPerFinding"/> is <see cref="UsdSpent"/> / Found; null when Found is zero.
/// </summary>
public sealed record StageOutcomeSummaryRowDto(
    OrchestrationStage Stage,
    int Runs,
    int Found,
    int Clean,
    int Skipped,
    int Failed,
    int Unreported,
    decimal? HitPercent,
    decimal UsdSpent,
    decimal? UsdPerFinding,
    int ServerSecs);

/// <summary>
/// POST /api/agent-tasks/{id}/finding (CARD-0272 S3). <see cref="Stage"/> is a name, not an ordinal,
/// so an unknown value is 422 rather than a bind 400.
/// </summary>
public sealed record RecordStageFindingRequest(
    string Stage,
    bool Found,
    string? Detail = null);

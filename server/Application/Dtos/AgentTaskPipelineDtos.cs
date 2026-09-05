using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Fleet-wide pipeline projection (CARD-0304). Advisory in-flight recommendations plus the
/// current in-flight / queued / blocked / ready-for-next-stage snapshot. Never a dispatch gate.
/// </summary>
public sealed record AgentTaskPipelineDto(
    DateTime AsOf,
    bool RecommendationsAreAdvisory,
    /// <summary>CARD-0031: <c>DelegationSettings.MaxConcurrentTasks</c>.</summary>
    int MaxConcurrentTasks,
    /// <summary>
    /// CARD-0031: non-Check Dispatched/Working count — the same predicate as the dispatcher's
    /// active-task query.
    /// </summary>
    int InFlightAgainstCap,
    IReadOnlyList<AgentTaskPipelineStageDto> Stages);

public sealed record AgentTaskPipelineStageDto(
    AgentTaskRole Role,
    int? RecommendedInFlight,
    int InFlightCount,
    bool AtOrAboveRecommendation,
    IReadOnlyList<AgentTaskPipelineInFlightDto> InFlight,
    IReadOnlyList<AgentTaskPipelineQueuedDto> Queued,
    IReadOnlyList<AgentTaskPipelineBlockedDto> Blocked,
    IReadOnlyList<AgentTaskPipelineReadyDto> Ready,
    /// <summary>
    /// CARD-0305: the stage-wide routing pin for this role, when one is active. Advisory like
    /// everything else here — reading the pipeline never applies or writes a pin.
    /// </summary>
    RoutingPinRefDto? RoutingPin = null);

public sealed record AgentTaskPipelineCardRefDto(Guid Id, string Identifier, string Title);

public sealed record AgentTaskPipelineInFlightDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskStatus Status,
    AgentTaskPipelineCardRefDto? Card,
    string? AgentName,
    DateTime? DispatchedAt,
    DateTime LastActivityAt,
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    WorkspaceMode Workspace);

public sealed record AgentTaskPipelineQueuedDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskPipelineCardRefDto? Card,
    DateTime CreatedAt,
    string QueueReason,
    IReadOnlyList<AgentTaskPipelineHolderDto> HeldBy,
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    WorkspaceMode Workspace);

public sealed record AgentTaskPipelineHolderDto(Guid TaskId, string ShortId, string Title);

public sealed record AgentTaskPipelineBlockedDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskPipelineCardRefDto? Card,
    DateTime CreatedAt,
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    /// <summary>CARD-0090: this Blocked row is routing-exhausted, not a question.</summary>
    bool RoutingExhausted = false);

public sealed record AgentTaskPipelineReadyDto(
    AgentTaskPipelineCardRefDto Card,
    Guid SourcePlanTaskId,
    string SourcePlanShortId,
    DateTime ReadySince,
    string DeliverablePath,
    string? DeliverableRef,
    /// <summary>
    /// CARD-0146 S4: the settled stage-role task that declared this ready row.
    /// </summary>
    AgentTaskRole SourceRole,
    /// <summary>
    /// CARD-0146 S4: the source task's <c>handoff:</c> line. Null on the legacy Plan→Code
    /// artifact bridge (a report that never got the handoff block parsed).
    /// </summary>
    string? Handoff = null,
    /// <summary>
    /// CARD-0305: the pin a dispatch of this ready row's stage would resolve through — the
    /// card's own pin for that role when it has one, else the stage-wide pin. Null when
    /// neither exists.
    /// </summary>
    RoutingPinRefDto? RoutingPin = null);

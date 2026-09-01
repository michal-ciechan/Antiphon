using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Fleet-wide pipeline projection (CARD-0304). Advisory in-flight recommendations plus the
/// current in-flight / queued / blocked / ready-for-Code snapshot. Never a dispatch gate.
/// </summary>
public sealed record AgentTaskPipelineDto(
    DateTime AsOf,
    bool RecommendationsAreAdvisory,
    IReadOnlyList<AgentTaskPipelineStageDto> Stages);

public sealed record AgentTaskPipelineStageDto(
    AgentTaskRole Role,
    int? RecommendedInFlight,
    int InFlightCount,
    bool AtOrAboveRecommendation,
    IReadOnlyList<AgentTaskPipelineInFlightDto> InFlight,
    IReadOnlyList<AgentTaskPipelineQueuedDto> Queued,
    IReadOnlyList<AgentTaskPipelineBlockedDto> Blocked,
    IReadOnlyList<AgentTaskPipelineReadyDto> Ready);

public sealed record AgentTaskPipelineCardRefDto(Guid Id, string Identifier, string Title);

public sealed record AgentTaskPipelineInFlightDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskStatus Status,
    AgentTaskPipelineCardRefDto? Card,
    string? AgentName,
    DateTime? DispatchedAt,
    DateTime LastActivityAt);

public sealed record AgentTaskPipelineQueuedDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskPipelineCardRefDto? Card,
    DateTime CreatedAt,
    string QueueReason,
    IReadOnlyList<AgentTaskPipelineHolderDto> HeldBy);

public sealed record AgentTaskPipelineHolderDto(Guid TaskId, string ShortId, string Title);

public sealed record AgentTaskPipelineBlockedDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskPipelineCardRefDto? Card,
    DateTime CreatedAt);

public sealed record AgentTaskPipelineReadyDto(
    AgentTaskPipelineCardRefDto Card,
    Guid SourcePlanTaskId,
    string SourcePlanShortId,
    DateTime ReadySince,
    string DeliverablePath,
    string? DeliverableRef);

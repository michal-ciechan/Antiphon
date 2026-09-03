using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record CreateScheduleRequest(
    string Name,
    ScheduleKind Kind = ScheduleKind.Prompt,
    ScheduleRepeat Repeat = ScheduleRepeat.Once,
    string? TimeZoneId = null,
    string? Agent = null,
    string? PromptText = null,
    ScheduleWhenTargetDown? WhenTargetDown = null,
    DateTime? FireAt = null,
    int? EveryMinutes = null,
    DateTime? AnchorAt = null,
    string? AtLocal = null,
    int? DaysOfWeek = null,
    int? MissedGraceMinutes = null,
    string? CreatedBy = null,
    string? CardId = null,
    CardStatus? TargetStatus = null,
    ScheduleStart Start = ScheduleStart.None,
    bool AcceptSpend = false);

public sealed record PatchScheduleRequest(
    Guid ConcurrencyToken,
    string? Name = null,
    bool? Enabled = null,
    string? TimeZoneId = null,
    string? PromptText = null,
    ScheduleWhenTargetDown? WhenTargetDown = null,
    DateTime? FireAt = null,
    int? EveryMinutes = null,
    DateTime? AnchorAt = null,
    string? AtLocal = null,
    int? DaysOfWeek = null,
    int? MissedGraceMinutes = null,
    ScheduleRepeat? Repeat = null);

public sealed record ScheduleDto(
    Guid Id,
    string Name,
    ScheduleKind Kind,
    ScheduleRepeat Repeat,
    string RepeatDescription,
    string TimeZoneId,
    DateTime? NextFireAt,
    DateTimeOffset? NextFireAtLocal,
    bool Enabled,
    int? MissedGraceMinutes,
    int FireCount,
    DateTime? LastFiredAt,
    ScheduleFireOutcome? LastOutcome,
    string? LastOutcomeDetail,
    string? CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid ConcurrencyToken,
    Guid? AgentId,
    string? AgentName,
    string? AgentSlug,
    string? PromptText,
    ScheduleWhenTargetDown WhenTargetDown,
    Guid? CardId,
    string? CardIdentifier,
    CardStatus? TargetStatus,
    ScheduleStart Start,
    DateTime? SpendAcceptedAt,
    string? SpendAcceptedBy,
    DateTime? FireAt,
    int? EveryMinutes,
    DateTime? AnchorAt,
    string? AtLocal,
    int DaysOfWeek,
    IReadOnlyList<ScheduleFireDto>? Fires = null);

public sealed record ScheduleFireDto(
    Guid Id,
    int FireNumber,
    DateTime DueAt,
    DateTime ClaimedAt,
    DateTime? CompletedAt,
    ScheduleFireOutcome Outcome,
    string? Detail,
    Guid? QueuedMessageId,
    Guid? SpawnedSessionId,
    bool Manual);

public sealed record ScheduleListDto(IReadOnlyList<ScheduleDto> Schedules);

public sealed record ScheduleOccurrenceDto(DateTime Utc, DateTimeOffset Local);

public sealed record ScheduleTargetDto(
    Guid? AgentId,
    string? AgentName,
    bool? AgentLive,
    bool? AgentAlwaysOn,
    string? SessionStatus,
    Guid? CardId,
    string? CardIdentifier,
    CardStatus? CardStatus,
    bool? CardArchived,
    string? CardColumn = null,
    Guid? CardOwnerSessionId = null);

public sealed record SchedulePreviewEnvironmentDto(
    bool OrchestratorPaused,
    int? BoardActiveCount = null,
    int? BoardCap = null,
    string? ModelAvailabilityHold = null,
    string? AssignedAgent = null,
    string? DefaultDefinition = null);

public sealed record SchedulePreviewDto(
    IReadOnlyList<ScheduleOccurrenceDto> NextOccurrences,
    ScheduleTargetDto Target,
    string Effect,
    string Spend,
    IReadOnlyList<string> Warnings,
    bool WillStartSession = false,
    string? WillMove = null,
    SchedulePreviewEnvironmentDto? Environment = null);

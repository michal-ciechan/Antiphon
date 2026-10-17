namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-1320. One standing orchestrator. GET and PUT fill the shortfall fields.
/// </summary>
public sealed record KeepSixOrchestratorDto(
    Guid AgentId,
    string Name,
    bool Enabled,
    int Target,
    long Revision,
    string? Reason,
    string? Provenance,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? EnabledAt,
    string? HoldReason,
    DateTimeOffset? HoldUntil,
    DateTimeOffset? LastReminderEnqueuedAt,
    Guid? OutstandingMessageId,
    DateTimeOffset? LastAcknowledgedAt,
    int? CountedOpen,
    int? EffectiveTarget,
    int? DispatchableBacklog,
    int? Shortfall,
    IReadOnlyList<string>? TopCards,
    DateTimeOffset? LastDispatchAt,
    DateTimeOffset? LastCompletionAt,
    string? Badge);

public sealed record PutKeepSixRequest(
    long ExpectedRevision,
    bool Enabled,
    int Target,
    string Reason,
    string Provenance);

public sealed record HoldKeepSixRequest(string Reason, DateTimeOffset Until);

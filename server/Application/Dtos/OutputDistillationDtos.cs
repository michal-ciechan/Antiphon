using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record DistillationDto(
    Guid Id,
    Guid TaskId,
    string TaskShortId,
    Guid? DistillTaskId,
    Guid? QueuedMessageId,
    string? BundleStamp,
    OutputDistillerMode Mode,
    int RawChars,
    int DistilledChars,
    int WaitMs,
    decimal CostUsd,
    DistillationOutcome Outcome,
    string? MissingAnchors,
    DateTime CreatedAt,
    DistillationFeedback Feedback,
    string? FeedbackNote,
    string? FeedbackBy,
    DateTime? FeedbackAt,
    DateTime? FullReadAt,
    string? RawResult,
    string? DistilledResult)
{
    public DateTime? RequestedAt { get; init; }
    public DateTime? DeadlineAt { get; init; }
    public DateTime? DequeuedAt { get; init; }
    public DateTime? RunCreatedAt { get; init; }
    public DateTime? DecisionAt { get; init; }
    public int? QueueWaitMs { get; init; }
    public int? SpecialistWaitMs { get; init; }
    public int? CleanupMs { get; init; }
    public string? Reason { get; init; }
    public string? ExpiryPhase { get; init; }
    public string? AvailabilityAlias { get; init; }
    public AgentKind? AvailabilityKind { get; init; }
    public DateTime? AvailabilityObservedAt { get; init; }
}

public sealed record DistillationStatsDto(
    DateTime? Since,
    int Total,
    IReadOnlyDictionary<string, int> ByOutcome,
    IReadOnlyDictionary<string, int> ByFeedback,
    IReadOnlyDictionary<string, int> ByBundleStamp,
    double? MedianRatio,
    double? P90Ratio,
    IReadOnlyList<string> TopMissingAnchorClasses,
    double? FullReadRate,
    decimal CostUsd);

public sealed record DistillationFeedbackRequest(string Verdict, string? Note = null);

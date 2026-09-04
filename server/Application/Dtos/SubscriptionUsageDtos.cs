using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// One display-safe subscription-usage observation (CARD-0333 S1 / D5).
/// GET /api/subscription-usage projects <c>SubscriptionUsageReader</c> snapshots only —
/// never raw command output, subscription/profile keys, paths, credentials, or session ids.
/// Nulls are preserved: a missing plan label, remaining percent, or reset is JSON null,
/// not a manufactured 0/100 or omitted field.
/// </summary>
public sealed record SubscriptionUsageObservationDto(
    AgentKind Provider,
    string? PlanLabel,
    double? RemainingPercent,
    DateTime? ResetsAt,
    DateTime ObservedAt,
    TimeSpan Age);

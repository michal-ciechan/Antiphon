using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record AwayDigestTaskDto(
    Guid TaskId, string ShortId, string Title, string Detail, DateTime? At, decimal CostUsd, bool IsNew = false);
public sealed record AwayDigestCardDto(string Identifier, string Title, DateTime At);
public sealed record AwayDigestRunningDto(int Count, string? Title, DateTime? StartedAt, decimal? BiggestRootCostUsd);
public sealed record AwayDigestSpendDto(decimal SettledSpendUsd, decimal BiggestRootUsd, int RootsOverHalfBudget);
public sealed record AwayDigestSubscriptionDto(AgentKind Provider, double RemainingPercent, DateTime? ResetsAt);

public sealed record AwayDigestDto(
    DateTime SinceUtc,
    DateTime UntilUtc,
    bool FirstWindow,
    IReadOnlyList<AwayDigestTaskDto> NeedsYou,
    IReadOnlyList<AwayDigestTaskDto> Failed,
    IReadOnlyList<AwayDigestTaskDto> Finished,
    IReadOnlyList<AwayDigestCardDto> Review,
    AwayDigestRunningDto Running,
    AwayDigestSpendDto Spend,
    IReadOnlyList<AwayDigestSubscriptionDto> Subscription);

public sealed record AwayDigestSendRequest(Guid? ChannelId = null, DateTime? Since = null);
public sealed record AwayDigestSendResult(Guid? ChannelId, bool Sent, string? Reason = null);

using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0136: a launch that would trip the subscription-quota threshold. HTTP 409,
/// <c>code: subscription_quota_low</c>, with a <c>quota</c> problem-details extension.
/// </summary>
public sealed class SubscriptionQuotaLowException : HttpException
{
    public const string ErrorCode = "subscription_quota_low";

    public SubscriptionQuotaVerdict Verdict { get; }

    public SubscriptionQuotaLowException(SubscriptionQuotaVerdict verdict)
        : base(409, SubscriptionQuotaPolicy.FormatRefusal(verdict), ErrorCode, BuildExtensions(verdict))
    {
        Verdict = verdict;
    }

    private static IReadOnlyDictionary<string, object?> BuildExtensions(SubscriptionQuotaVerdict verdict) =>
        new Dictionary<string, object?>
        {
            ["quota"] = new SubscriptionQuotaProblemDto(
                verdict.Provider.ToString(),
                verdict.SubscriptionKey,
                verdict.PlanLabel,
                verdict.RemainingPercent,
                verdict.ResetsAt,
                MinutesToReset: verdict.TimeToReset is { } t
                    ? (int)Math.Round(t.TotalMinutes)
                    : 0,
                verdict.ObservedAt,
                verdict.RuleName),
        };
}

/// <summary>The <c>quota</c> problem-details extension. Property names camelCase on the wire.</summary>
public sealed record SubscriptionQuotaProblemDto(
    string Provider,
    string SubscriptionKey,
    string? PlanLabel,
    double RemainingPercent,
    DateTime? ResetsAt,
    int MinutesToReset,
    DateTime ObservedAt,
    string Rule);

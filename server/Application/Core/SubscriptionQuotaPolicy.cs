using System.Globalization;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0136: remaining-% relative to time-to-reset. Pure; no I/O. Null verdict is a pass.
/// The gate may only ever refuse on a fresh, positive reading (D4).
/// </summary>
public static class SubscriptionQuotaPolicy
{
    public static SubscriptionQuotaVerdict? Evaluate(
        SubscriptionUsageSnapshot? snapshot,
        SubscriptionQuotaGateSettings settings,
        DateTime now)
    {
        if (!settings.Enabled)
            return null;
        if (snapshot is null)
            return null;
        if (snapshot.Age > TimeSpan.FromMinutes(settings.MaxSampleAgeMinutes))
            return null;

        var timeToReset = snapshot.ResetsAt is DateTime resetsAt
            ? resetsAt - now
            : TimeSpan.FromMinutes(settings.AssumedMinutesToResetWhenUnknown);
        if (timeToReset <= TimeSpan.Zero)
            return null;

        foreach (var rule in settings.Rules)
        {
            if (snapshot.RemainingPercent <= rule.MaxRemainingPercent
                && timeToReset > TimeSpan.FromMinutes(rule.MinMinutesToReset))
            {
                return new SubscriptionQuotaVerdict(
                    snapshot.Provider,
                    snapshot.SubscriptionKey,
                    snapshot.PlanLabel,
                    snapshot.RemainingPercent,
                    snapshot.ResetsAt,
                    timeToReset,
                    snapshot.ObservedAt,
                    rule.Name);
            }
        }

        return null;
    }

    public static string FormatPercent(double remaining) =>
        Math.Abs(remaining - Math.Round(remaining)) < 0.05
            ? Math.Round(remaining).ToString("0", CultureInfo.InvariantCulture)
            : remaining.ToString("0.#", CultureInfo.InvariantCulture);

    public static string FormatTimeToReset(TimeSpan? timeToReset)
    {
        if (timeToReset is not { } ts || ts <= TimeSpan.Zero)
            return "0m";

        var parts = new List<string>(3);
        var days = (int)ts.TotalDays;
        if (days > 0)
            parts.Add($"{days}d");
        if (ts.Hours > 0)
            parts.Add($"{ts.Hours}h");
        if (ts.Minutes > 0 && days == 0)
            parts.Add($"{ts.Minutes}m");
        if (parts.Count == 0)
            parts.Add($"{Math.Max(1, (int)Math.Ceiling(ts.TotalMinutes))}m");
        return string.Join(' ', parts);
    }

    public static string FormatSentence(SubscriptionQuotaVerdict verdict)
    {
        var plan = string.IsNullOrWhiteSpace(verdict.PlanLabel)
            ? string.Empty
            : $" '{verdict.PlanLabel}'";
        return
            $"{verdict.Provider} subscription{plan} (key {ShortKey(verdict.SubscriptionKey)}) has "
            + $"{FormatPercent(verdict.RemainingPercent)}% remaining and resets in "
            + $"{FormatTimeToReset(verdict.TimeToReset)} (rule {verdict.RuleName})";
    }

    public static string FormatRefusal(SubscriptionQuotaVerdict verdict) =>
        FormatSentence(verdict)
        + ". Pick another agentKind/agent, or re-send with ignoreSubscriptionQuota=true to launch anyway.";

    public static string FormatOverride(SubscriptionQuotaVerdict verdict) =>
        FormatSentence(verdict) + "; launched anyway because ignoreSubscriptionQuota=true.";

    public static string FormatDispatchWarning(SubscriptionQuotaVerdict verdict) =>
        $"dispatched on {verdict.Provider} at {FormatPercent(verdict.RemainingPercent)}% remaining, "
        + $"resets in {FormatTimeToReset(verdict.TimeToReset)} "
        + "(quota gate was passed/overridden at create)";

    internal static string ShortKey(string key) =>
        key.Length <= 8 ? key : key[..4] + "...";
}

public sealed record SubscriptionQuotaVerdict(
    AgentKind Provider,
    string SubscriptionKey,
    string? PlanLabel,
    double RemainingPercent,
    DateTime? ResetsAt,
    TimeSpan? TimeToReset,
    DateTime ObservedAt,
    string RuleName);

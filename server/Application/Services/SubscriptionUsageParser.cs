using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure parser for TUI subscription-usage panels (CARD-0143). No DI, no DB. Input is the raw
/// buffer; escapes are stripped with <see cref="AnsiStripper.Clean"/>. Silence on a failed parse
/// is forbidden — the caller always writes a sample, <c>Unparsed</c> if nothing matched.
/// </summary>
public static class SubscriptionUsageParser
{
    public const int RawExcerptCapChars = 500;

    /// <summary>
    /// Codex: remaining. Measured CARD-0141 ("Weekly limit: 3% left").
    /// </summary>
    internal const bool CodexPercentageIsRemaining = true;

    /// <summary>
    /// Grok: UNMEASURED. Identity (treat the printed percent as remaining) until S5 settles
    /// polarity. Do NOT invert: getting this backwards inverts CARD-0136's switching decision.
    /// Cited: CARD-0136 recorded a 1% progress-bar reading with no "left"/"used" word, six days
    /// before an Aug-28 reset.
    /// </summary>
    internal const bool GrokPercentageIsRemaining = true;

    private static readonly Regex CodexRemaining = new(
        @"Weekly\s+limit:\s*(\d+(?:\.\d+)?)\s*%\s*left",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CodexReset = new(
        @"resets\s+(\d{1,2}:\d{2}\s+on\s+\d{1,2}\s+[A-Za-z]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GrokPlan = new(
        @"Weekly\s+limit\s+\(([^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GrokPercent = new(
        @"(\d+(?:\.\d+)?)\s*%",
        RegexOptions.CultureInvariant);

    private static readonly Regex GrokReset = new(
        @"Resets:\s*([A-Za-z]+\s+\d{1,2},\s*\d{1,2}:\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static SubscriptionUsageParseResult Parse(
        AgentKind kind,
        string? rawBuffer,
        TimeProvider time,
        TimeZoneInfo? hostTimeZone = null)
    {
        var stripped = AnsiStripper.Clean(rawBuffer) ?? string.Empty;
        var excerpt = Cap(stripped);
        var tz = hostTimeZone ?? TimeZoneInfo.Local;

        return kind switch
        {
            AgentKind.Codex => ParseCodex(stripped, excerpt, time, tz),
            AgentKind.Grok => ParseGrok(stripped, excerpt, time, tz),
            _ => Unparsed(excerpt),
        };
    }

    /// <summary>
    /// Per-kind polarity of the printed percentage. True = remaining (to 100). False = used
    /// (remaining = 100 − used). The measurement that establishes each value is cited beside
    /// the constant; Grok's is unmeasured and stays identity until S5.
    /// </summary>
    internal static bool PercentageIsRemaining(AgentKind kind) => kind switch
    {
        AgentKind.Codex => CodexPercentageIsRemaining, // CARD-0141: "3% left"
        AgentKind.Grok => GrokPercentageIsRemaining,   // UNMEASURED; identity pending S5
        _ => true,
    };

    internal static DateTime? InferResetUtc(string? raw, TimeProvider time, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var year = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Year;
        var local = TryParseResetLocal(raw, year);
        if (local is null)
            return null;

        var utc = ToUtc(local.Value, tz);
        if (utc > nowUtc)
            return utc;

        var nextYear = TryParseResetLocal(raw, year + 1);
        return nextYear is null ? utc : ToUtc(nextYear.Value, tz);
    }

    private static SubscriptionUsageParseResult ParseCodex(
        string stripped, string excerpt, TimeProvider time, TimeZoneInfo tz)
    {
        var remainingMatch = CodexRemaining.Match(stripped);
        if (!remainingMatch.Success
            || !double.TryParse(remainingMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return Unparsed(excerpt);
        }

        var remaining = NormalizeRemaining(percent, AgentKind.Codex);
        string? resetsRaw = null;
        var resetMatch = CodexReset.Match(stripped);
        if (resetMatch.Success)
            resetsRaw = resetMatch.Groups[1].Value.Trim();

        return new SubscriptionUsageParseResult(
            SubscriptionUsageParseStatus.Parsed,
            PlanLabel: null,
            RemainingPercent: remaining,
            ResetsAt: InferResetUtc(resetsRaw, time, tz),
            ResetsAtRaw: resetsRaw,
            RawExcerpt: Cap(MatchedRegion(stripped, remainingMatch, resetMatch.Success ? resetMatch : null)));
    }

    private static SubscriptionUsageParseResult ParseGrok(
        string stripped, string excerpt, TimeProvider time, TimeZoneInfo tz)
    {
        var planMatch = GrokPlan.Match(stripped);
        var percentMatch = GrokPercent.Match(stripped);
        if (!percentMatch.Success
            || !double.TryParse(percentMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return Unparsed(excerpt);
        }

        var remaining = NormalizeRemaining(percent, AgentKind.Grok);
        string? plan = planMatch.Success ? planMatch.Groups[1].Value.Trim() : null;
        string? resetsRaw = null;
        var resetMatch = GrokReset.Match(stripped);
        if (resetMatch.Success)
            resetsRaw = resetMatch.Groups[1].Value.Trim();

        return new SubscriptionUsageParseResult(
            SubscriptionUsageParseStatus.Parsed,
            PlanLabel: plan,
            RemainingPercent: remaining,
            ResetsAt: InferResetUtc(resetsRaw, time, tz),
            ResetsAtRaw: resetsRaw,
            RawExcerpt: Cap(MatchedRegion(stripped, planMatch.Success ? planMatch : percentMatch, resetMatch.Success ? resetMatch : percentMatch)));
    }

    private static double NormalizeRemaining(double percent, AgentKind kind)
    {
        var remaining = PercentageIsRemaining(kind) ? percent : 100.0 - percent;
        if (remaining < 0) return 0;
        if (remaining > 100) return 100;
        return remaining;
    }

    private static DateTime? TryParseResetLocal(string raw, int year)
    {
        var trimmed = raw.Trim();
        var withYear = $"{trimmed} {year}";
        string[] formats =
        [
            "HH:mm 'on' d MMM yyyy",
            "H:mm 'on' d MMM yyyy",
            "HH:mm 'on' dd MMM yyyy",
            "H:mm 'on' dd MMM yyyy",
            "MMMM d, HH:mm yyyy",
            "MMMM d, H:mm yyyy",
            "MMMM dd, HH:mm yyyy",
            "MMMM dd, H:mm yyyy",
        ];
        if (DateTime.TryParseExact(
                withYear, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        }

        return null;
    }

    private static DateTime ToUtc(DateTime unspecifiedLocal, TimeZoneInfo tz)
    {
        if (tz.IsInvalidTime(unspecifiedLocal))
            unspecifiedLocal = unspecifiedLocal.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, tz);
    }

    private static string MatchedRegion(string stripped, Match first, Match? second)
    {
        var start = first.Index;
        var end = first.Index + first.Length;
        if (second is { Success: true })
        {
            start = Math.Min(start, second.Index);
            end = Math.Max(end, second.Index + second.Length);
        }

        start = Math.Max(0, start);
        end = Math.Min(stripped.Length, end);
        return stripped[start..end];
    }

    private static SubscriptionUsageParseResult Unparsed(string excerpt) =>
        new(SubscriptionUsageParseStatus.Unparsed, null, null, null, null, excerpt);

    private static string Cap(string text)
    {
        if (text.Length <= RawExcerptCapChars)
            return text;
        return text[..RawExcerptCapChars];
    }
}

public sealed record SubscriptionUsageParseResult(
    SubscriptionUsageParseStatus Status,
    string? PlanLabel,
    double? RemainingPercent,
    DateTime? ResetsAt,
    string? ResetsAtRaw,
    string? RawExcerpt);

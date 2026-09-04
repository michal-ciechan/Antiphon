using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Second parse of a structurally-classified Wall stub (CARD-0022). Reset is only one arm —
/// the per-model cap has none. Pure: no clock, no database. <c>+ 2 minutes</c> is applied by
/// the recovery writer, not here.
/// </summary>
public static partial class UsageLimitWallParser
{
    public const string SessionLimitFixtureText =
        "You've hit your session limit · resets 6:10pm (Europe/London)";

    public const string FableModelCapIncidentText =
        "You've reached your Fable 5 limit. Run /usage-credits to continue or switch models with /model.";

    [GeneratedRegex(
        @"your\s+(Fable|Opus|Sonnet|Haiku)(?:\s+[\d.]+)?\s+limit",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedModelLimitRegex();

    [GeneratedRegex(
        @"resets?(?:\s+at)?\s+(\d{1,2}):(\d{2})\s*(am|pm)?(?:\s*\(([^)]+)\))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetRegex();

    /// <summary>
    /// Parse Wall stub text. Returns null when no model alias can be resolved (neither in the
    /// text nor via <paramref name="fallbackAlias"/>) — the caller must not write a hold.
    /// Unparseable reset degrades to <see cref="UsageLimitWallKind.ModelCap"/>, never the
    /// 30-minute ladder.
    /// </summary>
    public static UsageLimitWall? Parse(DateTime nowUtc, string? text, string? fallbackAlias)
    {
        var raw = text ?? string.Empty;
        var fromText = ExtractModelAlias(raw);
        var alias = fromText ?? CanonicalOrNull(fallbackAlias);
        if (alias is null)
            return null;

        var reset = TryParseReset(nowUtc, raw);
        var kind = reset is { } parsed
            ? UsageLimitWallKind.SessionLimit
            : UsageLimitWallKind.ModelCap;

        return new UsageLimitWall(
            kind,
            alias,
            reset?.AtUtc,
            reset?.ZoneId,
            raw);
    }

    /// <summary>
    /// CARD-0281: Grok's own credit-limit vocabulary (from the 1.0.13 binary) plus the card's
    /// "exhausted credits" / "monthly spending limit". Applied only to a stub that already
    /// carries <c>IsApiError=true</c> and (for the 403 arm) HTTP 403.
    /// </summary>
    public static bool LooksLikeCapacity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var raw = text.AsSpan();
        return ContainsIgnoreCase(raw, "spending-limit")
            || ContainsIgnoreCase(raw, "spending limit")
            || ContainsIgnoreCase(raw, "out of credits")
            || ContainsIgnoreCase(raw, "usage balance exhausted")
            || ContainsIgnoreCase(raw, "usage limit reached")
            || ContainsIgnoreCase(raw, "exhausted credits")
            || ContainsIgnoreCase(raw, "monthly spending limit");
    }

    public static string FormatReason(UsageLimitWall wall)
    {
        if (wall.Kind == UsageLimitWallKind.SessionLimit && wall.ResetAt is { } at)
        {
            var local = wall.ResetZoneId is { } zone && TryFindZone(zone, out var tz) && tz is not null
                ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), tz)
                : at;
            var zoneLabel = wall.ResetZoneId ?? "UTC";
            return $"session-limit resets {local:HH:mm} {zoneLabel}";
        }

        if (TryReadApiErrorStatus(wall.RawText, out var status, out var phrase, out var detail)
            && (status is 402 or 403 || LooksLikeCapacity(wall.RawText)))
        {
            var statusBit = status is int s
                ? $"HTTP {s}{(string.IsNullOrEmpty(phrase) ? "" : " " + phrase)}"
                : "capacity";
            var clipped = string.IsNullOrWhiteSpace(detail) ? "no reset stated" : $"{detail}; no reset stated";
            return $"{wall.ModelAlias} provider capacity ({statusBit}: {clipped})";
        }

        if (LooksLikeCapacity(wall.RawText))
            return $"{wall.ModelAlias} provider capacity (no reset stated)";

        var label = wall.ModelAlias switch
        {
            ModelAlias.Fable => "Fable 5",
            ModelAlias.Opus => "Opus",
            ModelAlias.Sonnet => "Sonnet",
            ModelAlias.Haiku => "Haiku",
            _ => wall.ModelAlias,
        };
        return $"{label} per-model cap (no reset stated)";
    }

    private static bool TryReadApiErrorStatus(
        string? text, out int? status, out string? phrase, out string? detail)
    {
        status = null;
        phrase = null;
        detail = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var stripped = RetrySuffixRegex().Replace(text.Trim(), "");
        var match = ApiErrorStatusRegex().Match(stripped);
        if (!match.Success)
            return false;
        status = int.TryParse(match.Groups[1].Value, out var code) ? code : null;
        phrase = match.Groups[2].Value.Trim();
        detail = match.Groups[3].Value.Trim();
        if (detail.Length > 80)
            detail = detail[..80].Trim();
        return status is not null;
    }

    /// <summary>
    /// CARD-0360: Grok TurnEnd text may carry <c> [after N retries]</c>. Strip it so a 402
    /// status/phrase/detail parse is byte-identical to the unsuffixed form.
    /// </summary>
    [GeneratedRegex(@"\s+\[after \d+ retries\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex RetrySuffixRegex();

    private static bool ContainsIgnoreCase(ReadOnlySpan<char> haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(
        @"API error \(status (\d{3})\s*([^)]*)\):\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApiErrorStatusRegex();

    private static string? ExtractModelAlias(string text)
    {
        var match = NamedModelLimitRegex().Match(text);
        if (!match.Success)
            return null;
        return ModelAlias.Normalize(AgentKind.ClaudeCode, match.Groups[1].Value);
    }

    private static string? CanonicalOrNull(string? fallback)
    {
        if (string.IsNullOrWhiteSpace(fallback))
            return null;
        return ModelAlias.Normalize(AgentKind.ClaudeCode, fallback)
            ?? (IsKnownAlias(fallback) ? fallback.Trim().ToLowerInvariant() : null);
    }

    private static bool IsKnownAlias(string raw)
    {
        var folded = raw.Trim().ToLowerInvariant();
        foreach (var (_, alias) in ModelAlias.DelegatableAliases)
        {
            if (string.Equals(alias, folded, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static (DateTime AtUtc, string? ZoneId)? TryParseReset(DateTime nowUtc, string text)
    {
        var match = ResetRegex().Match(text);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var hour)
            || !int.TryParse(match.Groups[2].Value, out var minute))
            return null;
        if (minute is < 0 or > 59)
            return null;

        var ampm = match.Groups[3].Value;
        if (!string.IsNullOrEmpty(ampm))
        {
            if (hour is < 1 or > 12)
                return null;
            var isPm = ampm.Equals("pm", StringComparison.OrdinalIgnoreCase);
            hour = (hour % 12) + (isPm ? 12 : 0);
        }
        else if (hour is < 0 or > 23)
        {
            return null;
        }

        var zoneId = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null;
        if (!TryFindZone(zoneId, out var zone) || zone is null)
            return null;

        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var candidate = new DateTime(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, hour, minute, 0, DateTimeKind.Unspecified);
        // Next occurrence at-or-after now, in the named zone.
        if (candidate < nowLocal)
            candidate = candidate.AddDays(1);

        try
        {
            var asUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
            return (asUtc, zoneId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static bool TryFindZone(string? zoneId, out TimeZoneInfo? zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(zoneId))
            return true;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows sometimes only knows the Microsoft id for London.
            if (zoneId.Equals("Europe/London", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    zone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    return true;
                }
                catch (TimeZoneNotFoundException)
                {
                    zone = null;
                    return false;
                }
            }

            zone = null;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }
}

/// <param name="ResetAt">UTC. SessionLimit only; null means ModelCap.</param>
public sealed record UsageLimitWall(
    UsageLimitWallKind Kind,
    string ModelAlias,
    DateTime? ResetAt,
    string? ResetZoneId,
    string RawText);

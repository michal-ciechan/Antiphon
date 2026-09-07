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

    /// <summary>
    /// Measured 2026-09-05 on Claude (fable/haiku/opus/sonnet): 61 chars, middle-dot separator.
    /// The production transcript puts this on the sibling AssistantText; TurnEnd.Text is null.
    /// </summary>
    public const string SessionLimitProductionText =
        "You've hit your session limit · resets 5:20pm (Europe/London)";

    /// <summary>CARD-0412 production hour-only form (TurnEnd.Text null; sibling AssistantText).</summary>
    public const string SessionLimitHourOnlyNineAmText =
        "You've hit your session limit · resets 9am (Europe/London)";

    /// <summary>CARD-0412 production hour-only 2pm form.</summary>
    public const string SessionLimitHourOnlyTwoPmText =
        "You've hit your session limit · resets 2pm (Europe/London)";

    /// <summary>CARD-0412 grammar. Existing rows parsed under the minute-required regex are version 1.</summary>
    public const int ParseVersion = 2;

    public const string FableModelCapIncidentText =
        "You've reached your Fable 5 limit. Run /usage-credits to continue or switch models with /model.";

    [GeneratedRegex(
        @"your\s+(Fable|Opus|Sonnet|Haiku)(?:\s+[\d.]+)?\s+limit",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedModelLimitRegex();

    [GeneratedRegex(
        @"\bresets?(?:\s+at)?\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetLeadRegex();

    /// <summary>Hour-only AM/PM. Omitted minutes mean zero only in this branch (CARD-0412).</summary>
    [GeneratedRegex(
        @"^(?<hour>\d{1,2})\s*(?<ampm>am|pm)(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HourOnlyResetRegex();

    [GeneratedRegex(
        @"^(?<hour>\d{1,2}):(?<minute>\d{2})(?:\s*(?<ampm>am|pm))?(?!\w|:|\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HourMinuteResetRegex();

    [GeneratedRegex(
        @"^\s*\((?<zone>[^)]+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ClosedZoneRegex();

    /// <summary>
    /// Parse Wall stub text against the evidence instant rather than wall-clock now.
    /// Returns null when no model alias can be resolved (neither in the
    /// text nor via <paramref name="fallbackAlias"/>) — the caller must not write a hold.
    /// Unparseable reset degrades to <see cref="UsageLimitWallKind.ModelCap"/>, never the
    /// 30-minute ladder. A supplied-but-invalid reset is distinguished from no reset stated.
    /// </summary>
    public static UsageLimitWall? Parse(DateTime evidenceAtUtc, string? text, string? fallbackAlias)
    {
        var raw = text ?? string.Empty;
        var fromText = ExtractModelAlias(raw);
        var alias = fromText ?? CanonicalOrNull(fallbackAlias);
        if (alias is null)
            return null;

        var reset = TryParseReset(evidenceAtUtc, raw);
        var kind = reset.Status == ResetParseStatus.Valid
            ? UsageLimitWallKind.SessionLimit
            : UsageLimitWallKind.ModelCap;

        return new UsageLimitWall(
            kind,
            alias,
            reset.AtUtc,
            reset.ZoneId,
            raw,
            reset.Status == ResetParseStatus.Invalid ? reset.Failure : null);
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

        var resetBit = wall.ResetParseFailure is { } failure
            ? $"reset unparseable: {failure}"
            : "no reset stated";

        if (TryReadApiErrorStatus(wall.RawText, out var status, out var phrase, out var detail)
            && (status is 402 or 403 || LooksLikeCapacity(wall.RawText)))
        {
            var statusBit = status is int s
                ? $"HTTP {s}{(string.IsNullOrEmpty(phrase) ? "" : " " + phrase)}"
                : "capacity";
            var clipped = string.IsNullOrWhiteSpace(detail) ? resetBit : $"{detail}; {resetBit}";
            return $"{wall.ModelAlias} provider capacity ({statusBit}: {clipped})";
        }

        if (LooksLikeCapacity(wall.RawText))
            return $"{wall.ModelAlias} provider capacity ({resetBit})";

        var label = wall.ModelAlias switch
        {
            ModelAlias.Fable => "Fable 5",
            ModelAlias.Opus => "Opus",
            ModelAlias.Sonnet => "Sonnet",
            ModelAlias.Haiku => "Haiku",
            _ => wall.ModelAlias,
        };
        return $"{label} per-model cap ({resetBit})";
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

    private enum ResetParseStatus
    {
        Absent,
        Valid,
        Invalid,
    }

    private readonly record struct ResetParse(
        ResetParseStatus Status,
        DateTime? AtUtc,
        string? ZoneId,
        string? Failure)
    {
        public static ResetParse Absent { get; } = new(ResetParseStatus.Absent, null, null, null);

        public static ResetParse Invalid(string failure) =>
            new(ResetParseStatus.Invalid, null, null, failure);

        public static ResetParse Valid(DateTime atUtc, string? zoneId) =>
            new(ResetParseStatus.Valid, atUtc, zoneId, null);
    }

    private static ResetParse TryParseReset(DateTime evidenceAtUtc, string text)
    {
        var lead = ResetLeadRegex().Match(text);
        if (!lead.Success)
            return ResetParse.Absent;

        var rest = text[(lead.Index + lead.Length)..];
        var hourOnly = HourOnlyResetRegex().Match(rest);
        var hourMinute = HourMinuteResetRegex().Match(rest);
        int hour;
        int minute;
        string ampm;
        int consumed;
        if (hourOnly.Success)
        {
            // Omitted minutes mean zero only in this valid hour-only AM/PM branch.
            if (!int.TryParse(hourOnly.Groups["hour"].Value, out hour))
                return ResetParse.Invalid("malformed reset hour");
            minute = 0;
            ampm = hourOnly.Groups["ampm"].Value;
            consumed = hourOnly.Length;
        }
        else if (hourMinute.Success)
        {
            if (!int.TryParse(hourMinute.Groups["hour"].Value, out hour)
                || !int.TryParse(hourMinute.Groups["minute"].Value, out minute))
            {
                return ResetParse.Invalid("malformed reset time");
            }

            ampm = hourMinute.Groups["ampm"].Value;
            consumed = hourMinute.Length;
        }
        else
        {
            return ResetParse.Invalid("malformed reset time");
        }

        if (minute is < 0 or > 59)
            return ResetParse.Invalid("invalid reset minutes");

        if (!string.IsNullOrEmpty(ampm))
        {
            if (hour is < 1 or > 12)
                return ResetParse.Invalid("invalid 12-hour reset hour");
            var isPm = ampm.Equals("pm", StringComparison.OrdinalIgnoreCase);
            hour = (hour % 12) + (isPm ? 12 : 0);
        }
        else if (hour is < 0 or > 23)
        {
            return ResetParse.Invalid("invalid 24-hour reset hour");
        }

        rest = rest[consumed..];
        string? zoneId = null;
        var zoneMatch = ClosedZoneRegex().Match(rest);
        if (zoneMatch.Success)
        {
            zoneId = zoneMatch.Groups["zone"].Value.Trim();
            if (string.IsNullOrWhiteSpace(zoneId))
                return ResetParse.Invalid("empty reset zone");
        }
        else
        {
            var trimmed = rest.TrimStart();
            if (trimmed.StartsWith('('))
                return ResetParse.Invalid("unclosed or empty reset zone");
        }

        if (!TryFindZone(zoneId, out var zone) || zone is null)
            return ResetParse.Invalid(
                string.IsNullOrWhiteSpace(zoneId)
                    ? "invalid reset zone"
                    : $"invalid zone '{zoneId}'");

        var clock = ResolveResetUtc(evidenceAtUtc, zone, hour, minute);
        if (clock.Failure is { } failure)
            return ResetParse.Invalid(failure);
        return ResetParse.Valid(clock.AtUtc!.Value, zoneId);
    }

    private static ResetParse ResolveResetUtc(
        DateTime evidenceAtUtc, TimeZoneInfo zone, int hour, int minute)
    {
        var evidenceUtc = DateTime.SpecifyKind(evidenceAtUtc, DateTimeKind.Utc);
        var evidenceLocal = TimeZoneInfo.ConvertTimeFromUtc(evidenceUtc, zone);
        var day = evidenceLocal.Date;
        var candidate = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(candidate))
            return ResetParse.Invalid("nonexistent local time");

        for (var i = 0; i < 2; i++)
        {
            candidate = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(candidate))
            {
                day = day.AddDays(1);
                continue;
            }

            var options = UtcInterpretations(zone, candidate)
                .Where(u => u >= evidenceUtc)
                .ToList();
            if (options.Count > 0)
            {
                // Ambiguous local times choose the later UTC occurrence among those at-or-after evidence.
                return ResetParse.Valid(options.Max(), null);
            }

            day = day.AddDays(1);
        }

        return ResetParse.Invalid("reset not representable after evidence");
    }

    private static IEnumerable<DateTime> UtcInterpretations(TimeZoneInfo zone, DateTime localUnspecified)
    {
        if (zone.IsInvalidTime(localUnspecified))
            yield break;
        if (zone.IsAmbiguousTime(localUnspecified))
        {
            foreach (var offset in zone.GetAmbiguousTimeOffsets(localUnspecified))
                yield return new DateTimeOffset(localUnspecified, offset).UtcDateTime;
            yield break;
        }

        DateTime utc;
        try
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(localUnspecified, zone);
        }
        catch (ArgumentException)
        {
            yield break;
        }

        yield return utc;
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
/// <param name="ResetParseFailure">
/// Set when a reset token was supplied but could not be parsed (invalid zone, DST gap,
/// malformed time). Distinct from a genuinely absent reset.
/// </param>
public sealed record UsageLimitWall(
    UsageLimitWallKind Kind,
    string ModelAlias,
    DateTime? ResetAt,
    string? ResetZoneId,
    string RawText,
    string? ResetParseFailure = null);

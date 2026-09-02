using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The whole calendar for a <see cref="Schedule"/> (CARD-0057 D2). Static, side-effect-free,
/// table-tested. Daily walks forward up to eight local days; a spring-forward gap rolls to the
/// first valid minute after it; a fall-back overlap takes the first occurrence.
/// </summary>
public static class ScheduleRecurrence
{
    public const int MondayBit = 1 << 0;
    public const int AllDaysMask = 0b1111111;
    public const int MinEveryMinutes = 1;
    public const int MaxEveryMinutes = 10_080;

    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    /// <summary>
    /// The next occurrence strictly after <paramref name="nowUtc"/>, or the Once instant when it
    /// is still at/after now. Once in the past returns null — create uses
    /// <see cref="InitialNextFireAt"/> so an overdue Once is still claimed.
    /// </summary>
    public static DateTime? NextAfter(Schedule schedule, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        nowUtc = AsUtc(nowUtc);
        return schedule.Repeat switch
        {
            ScheduleRepeat.Once => NextOnce(schedule, nowUtc),
            ScheduleRepeat.Interval => NextInterval(schedule, nowUtc),
            ScheduleRepeat.Daily => NextDaily(schedule, nowUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule), schedule.Repeat, "Unknown repeat."),
        };
    }

    /// <summary>
    /// What to stamp on a new or re-enabled row. Once uses <see cref="Schedule.FireAt"/> even
    /// when it is already past so downtime still produces the one late claim; recurring uses
    /// <see cref="NextAfter"/> from now (paused-for-a-week does not fire as "late" on resume).
    /// </summary>
    public static DateTime? InitialNextFireAt(Schedule schedule, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        nowUtc = AsUtc(nowUtc);
        if (schedule.Repeat == ScheduleRepeat.Once && schedule.FireAt is DateTime fireAt)
            return AsUtc(fireAt);
        return NextAfter(schedule, nowUtc);
    }

    /// <summary>The next <paramref name="count"/> occurrences from <paramref name="nowUtc"/> (preview).</summary>
    public static IReadOnlyList<DateTime> NextOccurrences(Schedule schedule, DateTime nowUtc, int count)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (count <= 0)
            return [];

        nowUtc = AsUtc(nowUtc);
        var result = new List<DateTime>(count);
        var cursor = InitialNextFireAt(schedule, nowUtc);
        while (cursor is DateTime due && result.Count < count)
        {
            result.Add(due);
            var after = due.AddTicks(1);
            cursor = NextAfter(schedule, after);
        }

        return result;
    }

    public static string Describe(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var zone = string.IsNullOrWhiteSpace(schedule.TimeZoneId) ? "UTC" : schedule.TimeZoneId;
        return schedule.Repeat switch
        {
            ScheduleRepeat.Once when schedule.FireAt is DateTime fireAt =>
                $"once {AsUtc(fireAt):yyyy-MM-dd HH:mm} UTC",
            ScheduleRepeat.Interval when schedule.EveryMinutes is int every =>
                every == 1 ? "every 1 min" : $"every {every} min",
            ScheduleRepeat.Daily => DescribeDaily(schedule, zone),
            _ => schedule.Repeat.ToString().ToLowerInvariant(),
        };
    }

    public static bool TryGetTimeZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static TimeZoneInfo RequireTimeZone(string? timeZoneId)
    {
        if (TryGetTimeZone(timeZoneId, out var zone))
            return zone;
        throw new ArgumentException(
            $"Unknown time zone '{timeZoneId}'. Use an IANA or Windows id that TimeZoneInfo resolves.",
            nameof(timeZoneId));
    }

    public static int EffectiveDaysOfWeek(int daysOfWeek) =>
        daysOfWeek == 0 ? AllDaysMask : daysOfWeek & AllDaysMask;

    public static bool MaskContains(int daysOfWeek, DayOfWeek dayOfWeek)
    {
        var mask = EffectiveDaysOfWeek(daysOfWeek);
        var bit = BitFor(dayOfWeek);
        return (mask & bit) != 0;
    }

    public static int BitFor(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => 1 << 0,
        DayOfWeek.Tuesday => 1 << 1,
        DayOfWeek.Wednesday => 1 << 2,
        DayOfWeek.Thursday => 1 << 3,
        DayOfWeek.Friday => 1 << 4,
        DayOfWeek.Saturday => 1 << 5,
        DayOfWeek.Sunday => 1 << 6,
        _ => 0,
    };

    public static int? DefaultMissedGraceMinutes(ScheduleRepeat repeat, int? everyMinutes) =>
        repeat switch
        {
            ScheduleRepeat.Once => null,
            ScheduleRepeat.Daily => 60,
            ScheduleRepeat.Interval => Math.Min(Math.Max(everyMinutes ?? 60, 1), 60),
            _ => 60,
        };

    private static DateTime? NextOnce(Schedule schedule, DateTime nowUtc)
    {
        if (schedule.FireAt is not DateTime fireAt)
            return null;
        fireAt = AsUtc(fireAt);
        return fireAt >= nowUtc ? fireAt : null;
    }

    private static DateTime? NextInterval(Schedule schedule, DateTime nowUtc)
    {
        if (schedule.EveryMinutes is not int every || every < MinEveryMinutes)
            return null;
        if (schedule.AnchorAt is not DateTime anchor)
            return null;

        anchor = AsUtc(anchor);
        if (anchor > nowUtc)
            return anchor;

        var elapsedMinutes = (nowUtc - anchor).TotalMinutes;
        var steps = (long)Math.Floor(elapsedMinutes / every);
        var candidate = anchor.AddMinutes(steps * (double)every);
        if (candidate <= nowUtc)
            candidate = candidate.AddMinutes(every);
        return candidate;
    }

    private static DateTime? NextDaily(Schedule schedule, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(schedule.AtLocal)
            || !TimeOnly.TryParse(schedule.AtLocal, out var at))
        {
            return null;
        }

        if (!TryGetTimeZone(schedule.TimeZoneId, out var zone))
            return null;

        var mask = EffectiveDaysOfWeek(schedule.DaysOfWeek);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        for (var i = 0; i <= 8; i++)
        {
            var day = DateOnly.FromDateTime(localNow).AddDays(i);
            if ((mask & BitFor(day.DayOfWeek)) == 0)
                continue;

            var local = DateTime.SpecifyKind(
                day.ToDateTime(at),
                DateTimeKind.Unspecified);
            var utc = LocalWallToUtc(zone, local);
            if (utc > nowUtc)
                return utc;
        }

        return null;
    }

    /// <summary>
    /// Convert a local unspecified wall time. Invalid (spring-forward gap) rolls forward a minute
    /// at a time until valid. Ambiguous (fall-back) takes the first occurrence — the larger offset,
    /// which is still daylight time.
    /// </summary>
    public static DateTime LocalWallToUtc(TimeZoneInfo zone, DateTime localUnspecified)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            var rolled = local;
            for (var i = 0; i < 180; i++)
            {
                rolled = rolled.AddMinutes(1);
                if (!zone.IsInvalidTime(rolled))
                    return TimeZoneInfo.ConvertTimeToUtc(rolled, zone);
            }

            throw new InvalidOperationException(
                $"Could not roll '{localUnspecified:O}' forward out of a DST gap in '{zone.Id}'.");
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var first = offsets[0];
            for (var i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] > first)
                    first = offsets[i];
            }

            return new DateTimeOffset(local, first).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTimeOffset ToLocal(DateTime utc, string timeZoneId)
    {
        utc = AsUtc(utc);
        if (!TryGetTimeZone(timeZoneId, out var zone))
            return new DateTimeOffset(utc, TimeSpan.Zero);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), zone);
    }

    private static string DescribeDaily(Schedule schedule, string zone)
    {
        var at = string.IsNullOrWhiteSpace(schedule.AtLocal) ? "00:00" : schedule.AtLocal;
        var days = DescribeDays(EffectiveDaysOfWeek(schedule.DaysOfWeek));
        return days is null
            ? $"daily {at} {zone}"
            : $"daily {at} {zone}, {days}";
    }

    private static string? DescribeDays(int mask)
    {
        if (mask == AllDaysMask)
            return null;

        var set = new bool[7];
        var count = 0;
        for (var i = 0; i < 7; i++)
        {
            set[i] = (mask & (1 << i)) != 0;
            if (set[i])
                count++;
        }

        if (count == 0)
            return null;

        if (mask == (MondayBit | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4)))
            return "Mon-Fri";

        if (count == 1)
        {
            for (var i = 0; i < 7; i++)
            {
                if (set[i])
                    return DayNames[i];
            }
        }

        return string.Join(",", Enumerable.Range(0, 7).Where(i => set[i]).Select(i => DayNames[i]));
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

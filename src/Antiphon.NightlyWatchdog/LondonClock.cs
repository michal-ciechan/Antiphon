namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-3: due-day identity is the Europe/London calendar date. The nightly is due at 00:30
/// London, overdue at 01:00 (30 min grace) and misses its morning deadline at 08:00 London. The previous
/// due day stays in scope until that deadline.
/// </summary>
public static class LondonClock
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(ResolveZone);

    public static TimeZoneInfo TimeZone => Zone.Value;

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new TimeZoneNotFoundException("Europe/London zone data is not available on this host.");
    }

    public static DateOnly DueDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), TimeZone));

    public static DateOnly DueDay(DateTimeOffset utc) => DueDay(utc.UtcDateTime);

    public static DateTime DueUtc(DateOnly day) => LocalToUtc(day, 0, 30);

    public static DateTime GraceEndUtc(DateOnly day, int graceMinutes = 30) => DueUtc(day).AddMinutes(graceMinutes);

    public static DateTime MorningDeadlineUtc(DateOnly day) => LocalToUtc(day, 8, 0);

    public static bool PreviousDayInScope(DateTime utc) => AsUtc(utc) < MorningDeadlineUtc(DueDay(utc));

    public static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime LocalToUtc(DateOnly day, int hour, int minute)
    {
        var local = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);
    }

    internal static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

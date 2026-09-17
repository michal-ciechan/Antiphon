using System.Globalization;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0552 D-12 / TestDesign default 4. The optional <c>HH:mm-HH:mm</c> active window: start
/// INCLUSIVE, end EXCLUSIVE, evaluated on the injected clock converted to <c>TimeZoneId</c>, and
/// <c>start &gt; end</c> wraps across midnight. Shared by the validator (which refuses an
/// unparseable window at boot) and the sweep (which evaluates it), so a window that boots is a
/// window the sweep understands.
/// </summary>
internal static class MutationAutoDispatchWindow
{
    public static bool TryParse(string? window, out TimeOnly start, out TimeOnly end)
    {
        start = default;
        end = default;
        if (string.IsNullOrWhiteSpace(window)) return false;
        var parts = window.Split('-');
        if (parts.Length != 2) return false;
        return TimeOnly.TryParseExact(parts[0].Trim(), "HH:mm", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out start)
            && TimeOnly.TryParseExact(parts[1].Trim(), "HH:mm", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out end);
    }

    public static bool TryResolveZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            zone = TimeZoneInfo.Utc;
            return true;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="utcNow"/> falls inside the configured window. An unset or
    /// unparseable window is always open — the ceiling the operator asked for is the budget.
    /// </summary>
    public static bool IsOpen(MutationAutoDispatchSettings settings, DateTimeOffset utcNow)
    {
        if (!TryParse(settings.ActiveWindow, out var start, out var end)) return true;
        TryResolveZone(settings.TimeZoneId, out var zone);
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
        return start <= end
            ? local >= start && local < end
            : local >= start || local < end;
    }
}

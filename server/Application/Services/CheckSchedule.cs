using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// When the next check-in on a delegate is due (CARD-0047 §1.5). A pure function of the settings,
/// the task's declared duration and how many checks have already run — no clock, no database — so
/// the backoff can be tested as arithmetic rather than inferred from a swept row.
/// </summary>
public static class CheckSchedule
{
    /// <summary>
    /// The gap before check number <paramref name="checkNumber"/> (1-based: the FIRST scheduled
    /// re-check after the initial one is number 1).
    ///
    /// <code>
    /// interval(n) = clamp( max(CheckMinIntervalMinutes, expected / 2) * 2^(n-1),
    ///                      .., CheckMaxIntervalMinutes )
    /// </code>
    ///
    /// So a 10-minute task checks at ~10m, 15m, 25m, 45m…; a task declared <c>-ExpectAbout 180</c>
    /// first checks at 3 h and then every half hour. The doubling is what keeps a long-running task
    /// from generating a wall of notes while still noticing a stall early.
    /// </summary>
    public static TimeSpan NextInterval(DelegationSettings settings, int expectedDurationMinutes, int checkNumber)
    {
        var floor = Math.Max(1, settings.CheckMinIntervalMinutes);
        var ceiling = Math.Max(floor, settings.CheckMaxIntervalMinutes);
        var baseMinutes = Math.Max(floor, Math.Max(1, expectedDurationMinutes) / 2);

        // Shift, not Math.Pow: at checkNumber 40 a double would still be finite but the minutes
        // would overflow the TimeSpan; clamping the exponent first keeps this honest arithmetic.
        var steps = Math.Clamp(checkNumber - 1, 0, 20);
        var minutes = Math.Min((long)ceiling, (long)baseMinutes << steps);
        return TimeSpan.FromMinutes(Math.Max(1, minutes));
    }
}

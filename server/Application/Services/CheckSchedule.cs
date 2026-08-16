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
    /// A Fibonacci ramp from <see cref="DelegationSettings.CheckMinIntervalMinutes"/>: interval(1)
    /// is the base, interval(2) is twice the base, and every interval after that is the sum of the
    /// previous two, capped at <see cref="DelegationSettings.CheckMaxIntervalMinutes"/>. With the
    /// shipped defaults (base 5, ceiling 60) that is <c>5, 10, 15, 25, 40, 60, 60, 60 …</c> — most of
    /// the ramp still lands inside the first couple of hours, instead of doubling's two steps to the
    /// ceiling (CARD-0061).
    ///
    /// <para><paramref name="expectedDurationMinutes"/> plays no part in this ramp — the declared
    /// duration only schedules the FIRST check (<c>DispatchedAt + ExpectedDurationMinutes</c>, see
    /// <c>AgentTaskDispatcher.ArmFirstCheck</c>); once checks are running, the ramp is fixed. The
    /// parameter stays on the signature so callers don't need to change.</para>
    /// </summary>
    public static TimeSpan NextInterval(DelegationSettings settings, int expectedDurationMinutes, int checkNumber)
    {
        var floor = Math.Max(1, settings.CheckMinIntervalMinutes);
        var ceiling = Math.Max(floor, settings.CheckMaxIntervalMinutes);

        // Clamp before iterating: the ramp always saturates at the ceiling within a handful of
        // steps, so a huge checkNumber (bad input, not a real check count) must not turn into a
        // huge loop.
        var n = Math.Clamp(checkNumber, 1, 200);

        var previous = (long)floor;                   // interval(1)
        var current = Math.Min(ceiling, floor * 2L);   // interval(2)
        for (var i = 3; i <= n; i++)
        {
            var next = Math.Min(ceiling, previous + current);
            previous = current;
            current = next;
        }

        return TimeSpan.FromMinutes(n == 1 ? previous : current);
    }
}

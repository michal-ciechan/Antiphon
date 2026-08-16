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

        var raw = n == 1 ? previous : current;
        return TimeSpan.FromMinutes(RoundInterval((int)raw));
    }

    /// <summary>
    /// Rounds a raw ramp value to a human-readable interval (CARD-0061 follow-up): below 30 minutes
    /// to the nearest 5, from 30 to 60 to the nearest 10, above 60 hard-capped at 60. Kept separate
    /// from the Fibonacci arithmetic above so the ramp and the rounding can be reasoned about and
    /// tested independently — this keeps intervals readable even if the base, the ramp or
    /// <see cref="DelegationSettings.CheckMinIntervalMinutes"/>/<see cref="DelegationSettings.CheckMaxIntervalMinutes"/>
    /// ever change.
    /// </summary>
    public static int RoundInterval(int minutes)
    {
        if (minutes > 60)
        {
            return 60;
        }

        var step = minutes >= 30 ? 10 : 5;
        var rounded = (int)(Math.Round(minutes / (double)step, MidpointRounding.AwayFromZero) * step);

        // A raw value under half the step (e.g. 1 or 2 minutes, only reachable via a degenerate
        // CheckMinIntervalMinutes config) would otherwise round down to zero, which is not a
        // rounding of "5 minutes" — it is a scheduling bug (re-arming into the past forever).
        return Math.Max(1, rounded);
    }
}

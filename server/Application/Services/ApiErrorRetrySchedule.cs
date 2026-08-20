using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Per-session retry ladder for a dead API-error turn (CARD-0072 S5a, spec §D3). Pure: no clock,
/// no database. Deliberately not <see cref="CheckSchedule"/> — its 5-minute rounding floor eats
/// the 1- and 3-minute rungs, and it answers a different question (back off a delegate that might
/// legitimately be busy vs. retry a session structurally proven dead-idle).
///
/// <para>A one-minute first rung against a one-minute sweep means the first retry lands 1–2
/// minutes after the stub. Against a 31-minute silence, acceptable — do not "fix" this floor
/// wondering why the first fire is late.</para>
/// </summary>
public static class ApiErrorRetrySchedule
{
    /// <summary>Transient / Unknown rungs in minutes: 1, 3, 5, 10, 30, 60, then 60 indefinitely.</summary>
    public static readonly int[] TransientRungsMinutes = [1, 3, 5, 10, 30, 60];

    /// <summary>
    /// Wall whose reset text is unparseable (every Wall until CARD-0022's parser) enters the
    /// Transient ladder at this rung (§D3).
    /// </summary>
    public const int WallEntryRungMinutes = 30;

    /// <summary>
    /// Delay before attempt <paramref name="attemptNumber"/> (1-based: the first resume is
    /// number 1). NeedsHuman never schedules. Wall enters at the 30-minute rung, then hourly.
    /// Transient and Unknown share 1, 3, 5, 10, 30, 60, then 60 indefinitely.
    /// </summary>
    public static TimeSpan? Interval(int attemptNumber, ApiErrorClassification classification)
    {
        if (classification == ApiErrorClassification.NeedsHuman)
            return null;

        var n = Math.Clamp(attemptNumber, 1, 200);

        if (classification == ApiErrorClassification.Wall)
            return TimeSpan.FromMinutes(n == 1 ? WallEntryRungMinutes : 60);

        var index = n - 1;
        var minutes = index < TransientRungsMinutes.Length
            ? TransientRungsMinutes[index]
            : TransientRungsMinutes[^1];
        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// Unknown is Transient with a conservative cap: after this many enqueued resumes, park.
    /// </summary>
    public static bool UnknownIsExhausted(int attemptCount, int cap) =>
        attemptCount >= Math.Max(1, cap);

    /// <summary>
    /// §D8 wall cap: 3 consecutive wall deaths on one session parks it, so a 30-minute nudge at a
    /// five-hour quota wall costs at most three cheap deliveries instead of ten.
    /// </summary>
    public static bool WallIsParked(int consecutiveWallDeaths, int cap) =>
        consecutiveWallDeaths >= Math.Max(1, cap);
}

using System.Globalization;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1663. Program's runner-alarm exclusion, read from the directory's mirror of the
/// <c>SessionRunnerStates</c> row: a retired or draining runner raises no outage alarm.
/// CARD-1666: a drain is excluded only up to <c>Alarms:RunnerDrainMaxMinutes</c>; retirement has no bound.
/// </summary>
public sealed class RunnerStateAlarmExclusion(PhoneHomeRunnerDirectory directory, IOptions<AlarmSettings> settings)
    : IRunnerAlarmExclusion
{
    public string? Excluded(string runnerId) => Reason(directory.DrainState(runnerId));

    /// <summary>"retired" before "draining": a retired row is also still draining.</summary>
    public static string? Reason(RunnerState? state) =>
        state?.RetiredAt is not null ? "retired" : state is { Draining: true } ? "draining" : null;

    public string? DrainOverran(string runnerId, DateTimeOffset now) =>
        Overran(directory.DrainState(runnerId), now, settings.Value.RunnerDrainMaxMinutes);

    public DateTimeOffset? DrainExpiresAt(string runnerId) =>
        ExpiresAt(directory.DrainState(runnerId), settings.Value.RunnerDrainMaxMinutes);

    public static DateTimeOffset? ExpiresAt(RunnerState? state, int maxMinutes) => null;

    /// <summary>
    /// Null within the bound (inclusive). A missing or future drain start cannot be aged, so it
    /// fails safe: the drain counts as overrun and the runner alarms.
    /// </summary>
    public static string? Overran(RunnerState? state, DateTimeOffset now, int maxMinutes)
    {
        if (Reason(state) != "draining")
            return null;
        var bound = maxMinutes.ToString(CultureInfo.InvariantCulture);
        if (state!.DrainedAt is not { } since || since > now)
            return $"Its drain start is unknown, so its age cannot be checked against the Alarms:RunnerDrainMaxMinutes bound of {bound} min; failing safe, the drain overran.";
        var age = now - since;
        if (age <= TimeSpan.FromMinutes(maxMinutes))
            return null;
        var minutes = age.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture);
        return $"It has been draining since {since:O} ({minutes} min), past the Alarms:RunnerDrainMaxMinutes bound of {bound} min: the drain overran.";
    }
}

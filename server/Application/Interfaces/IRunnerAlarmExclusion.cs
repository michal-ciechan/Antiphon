namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0726 D-5: a reason this runner must not alarm (draining, retired), or null.
/// Program registers the row-backed <c>RunnerStateAlarmExclusion</c> (CARD-1663).
/// </summary>
public interface IRunnerAlarmExclusion
{
    string? Excluded(string runnerId);

    /// <summary>
    /// CARD-1666 A-2: the outage-note sentence for a draining (not retired) runner whose drain is
    /// older than <c>Alarms:RunnerDrainMaxMinutes</c> or has no usable start, or null. An offline
    /// runner with this sentence alarms again; a retired runner never has one.
    /// </summary>
    string? DrainOverran(string runnerId, DateTimeOffset now);

    /// <summary>
    /// CARD-1671: when a draining (not retired) runner with a recorded drain start reaches
    /// <c>Alarms:RunnerDrainMaxMinutes</c>, or null. The alarm loop wakes just past it.
    /// </summary>
    DateTimeOffset? DrainExpiresAt(string runnerId);
}

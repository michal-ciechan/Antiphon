namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0726 D-5: a reason this runner must not alarm (draining, retired), or null.
/// Program registers the row-backed <c>RunnerStateAlarmExclusion</c> (CARD-1663).
/// </summary>
public interface IRunnerAlarmExclusion
{
    string? Excluded(string runnerId);
}

using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1663. Program's runner-alarm exclusion, read from the directory's mirror of the
/// <c>SessionRunnerStates</c> row: a retired or draining runner raises no outage alarm.
/// </summary>
public sealed class RunnerStateAlarmExclusion(PhoneHomeRunnerDirectory directory) : IRunnerAlarmExclusion
{
    public string? Excluded(string runnerId) => null;
}

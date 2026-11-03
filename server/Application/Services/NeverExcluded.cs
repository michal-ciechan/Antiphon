using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

public sealed class NeverExcluded : IRunnerAlarmExclusion
{
    public string? Excluded(string runnerId) => null;

    public string? DrainOverran(string runnerId, DateTimeOffset now) => null;

    public DateTimeOffset? DrainExpiresAt(string runnerId) => null;
}

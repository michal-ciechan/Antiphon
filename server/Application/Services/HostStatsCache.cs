using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public enum HostStatsFailureKind { Transient, Offline, Unsupported }

public sealed class HostStatsCache(HostStatsSettings settings, TimeProvider time)
{
    public void BeginTick() => throw new NotImplementedException();
    public void Record(string hostId, RunnerHostStatsDto dto) => throw new NotImplementedException();
    public void RecordFailure(string hostId, HostStatsFailureKind kind) => throw new NotImplementedException();
    public bool Changed => throw new NotImplementedException();
    public void Published() => throw new NotImplementedException();
    public IReadOnlyList<HostStatsDto> Project(
        IReadOnlyList<SessionRunnerCatalogueEntryDto> catalogue,
        IReadOnlyDictionary<string, HostStatsAntiphonDto>? counters = null) => throw new NotImplementedException();
}

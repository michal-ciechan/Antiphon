using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

public sealed record HostStatsAntiphonDto(
    int TasksInFlight,
    IReadOnlyDictionary<string, int> ByStage,
    IReadOnlyDictionary<string, int> ByKind,
    int Queued,
    int Held,
    int LandsPending,
    int? SessionsLive,
    int? SeatsDeclared,
    RunnerHostBuildSlotsDto? BuildSlots);

public sealed record HostStatsDto(
    string HostId,
    string DisplayName,
    string? Platform,
    string State,
    string? Reason,
    DateTimeOffset? ObservedAt,
    int? IntervalSeconds,
    int? Cores,
    RunnerHostCurrentDto? Current,
    IReadOnlyDictionary<string, RunnerHostWindowRollups>? Rollups,
    HostStatsAntiphonDto Antiphon);

public sealed record HostSeriesDto(
    string HostId,
    string Metric,
    string Window,
    int IntervalSeconds,
    IReadOnlyList<RunnerHostSeriesPoint> Points);

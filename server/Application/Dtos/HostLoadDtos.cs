using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

public sealed record HostLoadMetricSummary(double? Avg, double? Min, double? Max, double? P95,
    double? Sum, long ValidCount, long ExpectedCount, double Coverage, bool Complete,
    bool P95Approximate, double? P95ErrorBound, int HistogramVersion = 1, double? BinWidth = null);
public sealed record HostLoadMetricPoint(string Identity, DateTimeOffset At, double? Value);
public sealed record HostLoadHistogramState(double BinWidth, IReadOnlyDictionary<long, long> Bins,
    long Count, double Sum, double? Min, double? Max);
public sealed record HostLoadPressureInput(string? PhysicalHostKey, string? ExpectedPhysicalHostKey,
    DateTimeOffset? SampledAt, double? LatestCpu, double? Average15s, double? Average60s,
    int Valid15s = 3, int Expected15s = 3, int Valid60s = 12, int Expected60s = 12, bool BindingKnown = true);
public sealed record HostLoadCapacityInput(DateTimeOffset? ObservedAt, int? HostRemaining,
    int? RunnerSeatsRemaining, int? ProjectRemaining, int? StageRemaining);
public sealed record HostLoadLaunchObservation(Guid TaskId, int Attempt, int WindowOrdinal,
    DateTimeOffset AcceptedAt, bool Represented, bool StartupFailed);
public sealed record HostLoadRecommendationDto(int RecommendedAdditionalTasks, string Scope,
    IReadOnlyList<string> MissingConstraints, double? PressurePercent, double StartupDebtPercent,
    int CpuHeadroom, int? HostRemaining, int? RunnerSeatsRemaining, int? ProjectRemaining,
    int? StageRemaining, DateTimeOffset? InputsAt, IReadOnlyList<string> Reasons);
public sealed record HostLoadShadowDecision(double? CommandPressure, double? DispatchPressure,
    string Command, string Dispatch, IReadOnlyList<string> CommandReasons, IReadOnlyList<string> DispatchReasons);
public sealed record HostLoadAdvice(HostLoadShadowDecision Decision, HostLoadRecommendationDto Recommendation);
public sealed record HostLoadPolicyDto(string Mode, long? Revision, bool Shadow = true,
    double CommandThresholdPercent = 85, double DispatchThresholdPercent = 75, double PerTaskCpuPoints = 10);
public sealed record HostLoadCounters(DateTimeOffset? CountedAt, int? TasksInFlight,
    IReadOnlyDictionary<string, int>? ByStage, IReadOnlyDictionary<string, int>? ByKind,
    int? PendingLaunches, int? PendingMirrors, int? Queued, int? Held, int? SessionsLive, int? EffectiveTaskCapacity);
public sealed record HostLoadCurrentDto(int SchemaVersion, string HostId, string? PhysicalHostKey,
    DateTimeOffset ObservedAt, HostLoadSample? Current, double? CurrentAgeSeconds,
    IReadOnlyDictionary<string, HostLoadMetricSummary> LastHour, double Coverage,
    HostLoadPolicyDto Policy, HostAdmissionSnapshot Authority, HostLoadShadowDecision Decision,
    HostLoadRecommendationDto Recommendation);
public sealed record HostLoadHistoryDto(int SchemaVersion, string HostId, DateTimeOffset From, DateTimeOffset To,
    string Resolution, IReadOnlyList<object> Items, string? NextCursor, bool Complete, IReadOnlyList<string> Gaps);
public sealed record HostLoadBoundary(Guid EventId, DateTimeOffset EventAt, HostLoadSource? SampleIdentity,
    DateTimeOffset? SampleAt, double? AgeSeconds, string State);
public sealed record HostLoadTaskWindowDto(Guid TaskId, int Attempt, string PhysicalHostKey, Guid RunnerBootId,
    int WindowOrdinal, DateTimeOffset QueuedAt, DateTimeOffset? DispatchedAt, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, string State, HostLoadBoundary Start, HostLoadBoundary? End,
    IReadOnlyDictionary<string, HostLoadMetricSummary> During, int OverlapCount, string Attribution = "ambientHost");
public sealed record HostLoadTaskDto(int SchemaVersion, Guid TaskId, int Attempt,
    IReadOnlyList<HostLoadTaskWindowDto> Windows, bool Complete, IReadOnlyList<string> Gaps);

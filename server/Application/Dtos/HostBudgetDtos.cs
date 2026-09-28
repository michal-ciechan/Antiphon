namespace Antiphon.Server.Application.Dtos;

public sealed record HostLimit(
    string HostId, int? Configured, int? Declared, int? Effective, string Source,
    string? Reason = null, DateTime? UpdatedAt = null, int Revision = 0);

public sealed record RunnerCapacityDto(
    string RunnerId, int? DeclaredCapacity, int MaxCapacity, int? ConfiguredMaxInFlight,
    int? EffectiveLimit, int Occupied, bool Available, bool DispatchEligible);

public sealed record HostOccupiedBreakdownDto(int Sessions, int PendingLaunch, int InFlightMirrors);

public sealed record HostBudgetDto(
    string HostId, string Kind, int? ConfiguredMaxInFlight, int? DeclaredCapacity,
    int? EffectiveLimit, int InFlight, HostOccupiedBreakdownDto OccupiedBreakdown,
    bool Available, bool DispatchEligible, string Source, string? Reason,
    DateTime? UpdatedAt, int Revision);

public sealed record PutHostBudgetRequest(int? MaxInFlight, string? Reason);

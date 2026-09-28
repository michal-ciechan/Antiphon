namespace Antiphon.Server.Application.Dtos;

public sealed record HostLimit(
    string HostId, int? Configured, int? Declared, int? Effective, string Source,
    string? Reason = null, DateTime? UpdatedAt = null, int Revision = 0);

public sealed record RunnerCapacityDto(
    string RunnerId, int? DeclaredCapacity, int MaxCapacity, int? ConfiguredMaxInFlight,
    int? EffectiveLimit, int Occupied, bool Available, bool DispatchEligible);

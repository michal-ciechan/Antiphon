namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-1079: one stored occupancy sample. The row is detection history.
/// Reading it does not stop, kill, release or dispatch.
/// </summary>
public sealed record HostOccupancySampleDto(
    Guid Id,
    string HostId,
    DateTime SampledAt,
    string InventoryState,
    string? InventoryReason,
    int InFlight,
    int DispatchedWorking,
    int Sessions,
    int PendingLaunch,
    int InFlightMirrors,
    int IdleSeats,
    int PooledWarmSeats,
    int OrphanSlots,
    int? EffectiveLimit,
    int? DeclaredCapacity,
    DateTime? OldestIdleSince);

/// <summary>
/// Read-only occupancy audit for one host, newest first.
/// <see cref="Limit"/> is the applied cap: omitted means 500, and a larger request is clamped to 2000.
/// </summary>
public sealed record HostOccupancySamplesResponse(
    int Limit,
    DateTime From,
    DateTime To,
    IReadOnlyList<HostOccupancySampleDto> Samples);

using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1079: one immutable occupancy reading. <see cref="Empty"/> is the state before the
/// first successful publish. Detection only.
/// </summary>
public sealed record SeatOccupancySnapshot(DateTime GeneratedAt, IReadOnlyList<HostOccupancyObservation> Hosts)
{
    public static SeatOccupancySnapshot Empty { get; } = new(default, []);
}

/// <summary>
/// One host in a snapshot. <paramref name="Kind"/> is <c>local</c> or <c>runner</c>.
/// <paramref name="InventoryState"/> is <c>listed</c>, <c>unavailable</c>, or <c>local</c>.
/// </summary>
public sealed record HostOccupancyObservation(
    string HostId,
    string Kind,
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
    DateTime? OldestIdleSince,
    IReadOnlyList<SeatObservation> Seats);

/// <summary>
/// One runner seat joined to the desktop row. <see cref="Pushed"/> is <c>yes</c> or <c>unknown</c>.
/// <see cref="ParkState"/> is <c>none</c> or the owner's current-attempt park state; <see cref="ParkReason"/>
/// is that episode's reason, or null when there is no row.
/// </summary>
public sealed record SeatObservation(
    string RunnerId,
    Guid SessionId,
    string RunnerStatus,
    int? Pid,
    DateTime StartedAt,
    bool Occupies,
    bool Orphan,
    bool PooledWarm,
    string? DesktopStatus,
    Guid? TaskId,
    AgentTaskStatus? TaskStatus,
    int? Attempt,
    AgentTaskRole? Role,
    Guid? CardId,
    Guid? BoardId,
    Guid? AgentId,
    SeatClass Class,
    DateTime IdleSince,
    string Pushed,
    string ParkState = "none",
    string? ParkReason = null);

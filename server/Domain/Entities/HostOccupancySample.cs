namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-1079: one occupancy reading for a host at a sampler tick.
/// <see cref="InventoryState"/> is <c>listed</c>, <c>unavailable</c>, or <c>local</c>.
/// The row is detection history. It does not release or stop a session.
/// </summary>
public sealed class HostOccupancySample
{
    public Guid Id { get; set; }
    public string HostId { get; set; } = string.Empty;
    public DateTime SampledAt { get; set; }
    public string InventoryState { get; set; } = string.Empty;
    public string? InventoryReason { get; set; }
    public int InFlight { get; set; }
    public int DispatchedWorking { get; set; }
    public int Sessions { get; set; }
    public int PendingLaunch { get; set; }
    public int InFlightMirrors { get; set; }
    public int IdleSeats { get; set; }
    public int PooledWarmSeats { get; set; }
    public int OrphanSlots { get; set; }
    public int? EffectiveLimit { get; set; }
    public int? DeclaredCapacity { get; set; }
    public DateTime? OldestIdleSince { get; set; }
}

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-1079: idle-seat attention thresholds and the occupancy sampler cadence.
/// Detection only. Nothing here releases, kills or stops a session.
/// </summary>
public sealed class AttentionSettings
{
    public const string SectionName = "Attention";

    /// <summary>Off and the sampler publishes nothing and writes no rows.</summary>
    public bool SeatWatchEnabled { get; set; } = true;

    /// <summary>Age at which an idle seat or occupancy divergence becomes Warning. Must be positive.</summary>
    public int SeatIdleWarningMinutes { get; set; } = 30;

    /// <summary>Age at which those conditions become Error. Must exceed the warning age.</summary>
    public int SeatIdleErrorMinutes { get; set; } = 180;

    /// <summary>Sampler period, inclusive range 10..3600 seconds.</summary>
    public int OccupancySampleIntervalSeconds { get; set; } = 60;

    /// <summary>How long occupancy samples are kept, inclusive range 1..365 days.</summary>
    public int OccupancySampleRetentionDays { get; set; } = 14;

    /// <summary>Per-runner inventory call budget. Must be positive.</summary>
    public int InventoryTimeoutMs { get; set; } = 3000;
}

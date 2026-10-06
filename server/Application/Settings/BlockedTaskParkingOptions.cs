namespace Antiphon.Server.Application.Settings;

public sealed class BlockedTaskParkingOptions
{
    public const string SectionName = "BlockedTaskParking";
    public bool Enabled { get; set; } = false;
    public bool ReclaimExisting { get; set; } = false;

    /// <summary>Minimum gap between scheduled legacy sweeps. 0 sweeps on every call;
    /// a negative value is treated as 0. The overlap gate still applies.</summary>
    public int ReclaimIntervalSeconds { get; set; } = 120;
}

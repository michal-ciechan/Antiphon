namespace Antiphon.Server.Application.Settings;

public sealed class BlockedTaskParkingOptions
{
    public const string SectionName = "BlockedTaskParking";
    public bool Enabled { get; set; } = false;
    public bool ReclaimExisting { get; set; } = false;

    /// <summary>Minimum gap between scheduled legacy sweeps. 0 sweeps on every call;
    /// a negative value is treated as 0. The overlap gate still applies.</summary>
    public int ReclaimIntervalSeconds { get; set; } = 120;

    /// <summary>Delay before a Held episode is prepared again. 0 disables the backoff;
    /// a negative value is treated as 0. <c>park_workspace_reserved</c> stays immediate.</summary>
    public int ReclaimHeldBackoffSeconds { get; set; } = 600;
}

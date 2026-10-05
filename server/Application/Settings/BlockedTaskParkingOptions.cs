namespace Antiphon.Server.Application.Settings;

public sealed class BlockedTaskParkingOptions
{
    public const string SectionName = "BlockedTaskParking";
    public bool Enabled { get; set; } = false;
    public bool ReclaimExisting { get; set; } = false;
}

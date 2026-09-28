namespace Antiphon.Server.Domain.Entities;

public sealed class HostBudget
{
    public string HostId { get; set; } = string.Empty;
    public int? MaxInFlight { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public int Revision { get; set; }
}

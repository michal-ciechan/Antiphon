using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class AgentTuiValidationRun
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Guid ProfileRevisionId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public AgentTuiValidationStatus Status { get; set; }
    public string ResultsJson { get; set; } = "{}";
    public string CapabilitiesJson { get; set; } = "{}";
    public string? RunnerVersion { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public AgentTuiProfile Profile { get; set; } = null!;
    public AgentTuiProfileRevision ProfileRevision { get; set; } = null!;
}

using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class AgentTuiModel
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Family { get; set; }
    public AgentTuiModelSource Source { get; set; }
    public AgentTuiModelAvailability Availability { get; set; }
    public DateTime? DiscoveredAt { get; set; }
    public string? RunnerVersion { get; set; }
    public bool IsSuggestedDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AgentTuiProfile Profile { get; set; } = null!;
}

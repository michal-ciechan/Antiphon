using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class AgentTuiProfile
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public AgentKind Kind { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public AgentTuiProfileSource Source { get; set; }
    public string? SourceDefinitionName { get; set; }
    public Guid? ActiveRevisionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AgentTuiProfileRevision? ActiveRevision { get; set; }
    public ICollection<AgentTuiProfileRevision> Revisions { get; set; } = new List<AgentTuiProfileRevision>();
    public ICollection<AgentTuiSecret> Secrets { get; set; } = new List<AgentTuiSecret>();
    public ICollection<AgentTuiModel> Models { get; set; } = new List<AgentTuiModel>();
    public ICollection<AgentTuiValidationRun> ValidationRuns { get; set; } = new List<AgentTuiValidationRun>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
}

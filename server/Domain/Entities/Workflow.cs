using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A running instance of a workflow template, tracking execution state through stages.
/// </summary>
public class Workflow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid TemplateId { get; set; }
    public Guid ProjectId { get; set; }
    public WorkflowStatus Status { get; set; }
    public Guid? CurrentStageId { get; set; }
    public string InitialContext { get; set; } = string.Empty;
    public string? FeatureName { get; set; }
    public string GitBranchName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public WorkflowTemplate Template { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public Stage? CurrentStage { get; set; }
    public ICollection<Stage> Stages { get; set; } = new List<Stage>();
    public ICollection<GateDecision> GateDecisions { get; set; } = new List<GateDecision>();
    public ICollection<StageExecution> StageExecutions { get; set; } = new List<StageExecution>();
}

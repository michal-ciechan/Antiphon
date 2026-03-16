using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A single stage within a workflow, defining the executor, model, and gate configuration.
/// </summary>
public class Stage
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int StageOrder { get; set; }
    public StageStatus Status { get; set; }
    public string ExecutorType { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public bool GateRequired { get; set; }
    public int CurrentVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public Workflow Workflow { get; set; } = null!;
    public ICollection<GateDecision> GateDecisions { get; set; } = new List<GateDecision>();
    public ICollection<StageExecution> StageExecutions { get; set; } = new List<StageExecution>();
}

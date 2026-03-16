using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Records a single execution attempt of a stage, including version, cost, and git tag.
/// </summary>
public class StageExecution
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }
    public Guid WorkflowId { get; set; }
    public int Version { get; set; }
    public StageStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorDetails { get; set; }
    public string? GitTagName { get; set; }
    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    // Navigation properties
    public Stage Stage { get; set; } = null!;
    public Workflow Workflow { get; set; } = null!;
}

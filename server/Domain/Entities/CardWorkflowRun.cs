using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class CardWorkflowRun
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid PipelineDefinitionId { get; set; }
    public Guid PipelineDefinitionRevisionId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public CardWorkflowRunStatus Status { get; set; } = CardWorkflowRunStatus.Queued;
    public Guid? CurrentStageId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Card Card { get; set; } = null!;
    public PipelineDefinition PipelineDefinition { get; set; } = null!;
    public PipelineDefinitionRevision PipelineDefinitionRevision { get; set; } = null!;
    public CardWorkflowStage? CurrentStage { get; set; }
    public ICollection<CardWorkflowStage> Stages { get; set; } = new List<CardWorkflowStage>();
}

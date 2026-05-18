using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class CardWorkflowRun
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid AgentId { get; set; }
    public Guid? WorkflowTemplateId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public string WorkflowDefinitionSnapshot { get; set; } = string.Empty;
    public CardWorkflowRunStatus Status { get; set; } = CardWorkflowRunStatus.Queued;
    public Guid? CurrentStageId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Card Card { get; set; } = null!;
    public Agent Agent { get; set; } = null!;
    public WorkflowTemplate? WorkflowTemplate { get; set; }
    public CardWorkflowStage? CurrentStage { get; set; }
    public ICollection<CardWorkflowStage> Stages { get; set; } = new List<CardWorkflowStage>();
}

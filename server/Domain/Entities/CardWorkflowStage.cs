using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class CardWorkflowStage
{
    public Guid Id { get; set; }
    public Guid CardWorkflowRunId { get; set; }
    public int StageOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public AgentTaskRole Role { get; set; }
    public string BundleKey { get; set; } = string.Empty;
    public string AllowedNextJson { get; set; } = "[]";
    public CardWorkflowStageStatus Status { get; set; } = CardWorkflowStageStatus.Pending;
    public string? ResultSummary { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public CardWorkflowRun CardWorkflowRun { get; set; } = null!;
}

using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Guid? DefaultWorkflowTemplateId { get; set; }
    public AgentAssignmentPolicy AssignmentPolicy { get; set; } = AgentAssignmentPolicy.AutoPick;
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public string? PersistentSessionId { get; set; }
    public Guid? CurrentCardId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public WorkflowTemplate? DefaultWorkflowTemplate { get; set; }
    public Card? CurrentCard { get; set; }
    public ICollection<Card> QueueCards { get; set; } = new List<Card>();
    public ICollection<CardWorkflowRun> WorkflowRuns { get; set; } = new List<CardWorkflowRun>();
}

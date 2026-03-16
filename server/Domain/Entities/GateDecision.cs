using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Records a user's decision at a workflow gate point (approve, reject with feedback, or go back).
/// </summary>
public class GateDecision
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }
    public Guid WorkflowId { get; set; }
    public GateAction Action { get; set; }
    public string? Feedback { get; set; }
    public Guid DecidedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Stage Stage { get; set; } = null!;
    public Workflow Workflow { get; set; } = null!;
    public User DecidedByUser { get; set; } = null!;
}

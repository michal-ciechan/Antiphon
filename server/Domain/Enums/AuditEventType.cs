namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Types of audit events recorded in the audit trail.
/// </summary>
public enum AuditEventType
{
    LlmCall = 0,
    ToolInvocation = 1,
    StageStarted = 2,
    StageCompleted = 3,
    StageFailed = 4,
    GateApproved = 5,
    GateRejected = 6,
    GoBack = 7,
    UpdateBasedOnDiff = 8,
    UserPrompt = 9,
    WorkflowCreated = 10,
    WorkflowPaused = 11,
    WorkflowResumed = 12,
    WorkflowAbandoned = 13,
    WorkflowCompleted = 14
}

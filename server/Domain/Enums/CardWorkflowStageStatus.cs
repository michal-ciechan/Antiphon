namespace Antiphon.Server.Domain.Enums;

public enum CardWorkflowStageStatus
{
    Pending = 0,
    Running = 1,
    WaitingForHumanReview = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5
}

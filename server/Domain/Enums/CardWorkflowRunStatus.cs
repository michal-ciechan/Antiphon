namespace Antiphon.Server.Domain.Enums;

public enum CardWorkflowRunStatus
{
    Queued = 0,
    Running = 1,
    WaitingForHumanReview = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5
}

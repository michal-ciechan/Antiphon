namespace Antiphon.Server.Domain.Enums;

public enum WorkflowStatus
{
    Created = 0,
    Running = 1,
    Paused = 2,
    GateWaiting = 3,
    Completed = 4,
    Failed = 5,
    Abandoned = 6
}

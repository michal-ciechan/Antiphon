namespace Antiphon.Server.Domain.Enums;

public enum AgentStatus
{
    Idle = 0,
    Ready = 1,
    Working = 2,
    WaitingForHumanReview = 3,
    Stopped = 4,
    Disconnected = 5,
    Failed = 6
}

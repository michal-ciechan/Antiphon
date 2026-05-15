namespace Antiphon.Server.Domain.Enums;

public enum RunPhase
{
    PreparingWorkspace = 0,
    BuildingPrompt = 1,
    LaunchingAgent = 2,
    InitializingSession = 3,
    StreamingTurn = 4,
    Finishing = 5,
    Succeeded = 6,
    Failed = 7,
    TimedOut = 8,
    Stalled = 9,
    Canceled = 10
}

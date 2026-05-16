namespace Antiphon.Server.Application.Interfaces;

public enum AgentExitReason
{
    Unknown = 0,
    ProcessExited = 1,
    KilledByRequest = 2,
    MemoryKilled = 3
}

namespace Antiphon.Agents.Pty;

public enum PtyExitReason
{
    Unknown = 0,
    ProcessExited = 1,
    KilledByRequest = 2,
    MemoryKilled = 3
}

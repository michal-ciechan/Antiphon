namespace Antiphon.Server.Application.Interfaces;

public enum AgentExitReason
{
    Unknown = 0,
    ProcessExited = 1,
    KilledByRequest = 2,
    MemoryKilled = 3,

    /// <summary>
    /// The runner's CPU watchdog killed an IDLE session whose process was busy-looping a core
    /// (name-matched to RunnerExitReasons.CpuSpinKilled). The turn had completed — this is a
    /// clean stop, not a failure: the session stays resumable and a later message restarts it
    /// with --resume like any other stopped session.
    /// </summary>
    CpuSpinKilled = 4
}

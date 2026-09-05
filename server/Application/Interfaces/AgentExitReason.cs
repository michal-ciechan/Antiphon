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
    CpuSpinKilled = 4,

    /// <summary>Herdr adoption: pane missing or child pid gone (CARD-0186). Failed, never Stopped.</summary>
    HerdrRestartPresumedDead = 5,

    /// <summary>Herdr runtime close after the pane was proved gone (CARD-0186). Failed, never Stopped.</summary>
    HerdrPaneClosed = 6,

    /// <summary>Herdr unreachable at restart and the sidecar's child is OS-dead (CARD-0186). Failed.</summary>
    HerdrChildGone = 7,

    /// <summary>
    /// Herdr kill left the pane open because a foreign process was in it; our child was killed by
    /// pid (CARD-0186). Failed on the unsolicited exit path; operator KillAsync still lands Stopped.
    /// </summary>
    HerdrPaneLeftOpen = 8,

    /// <summary>
    /// CARD-0213: Stop on an attached pane dropped the sidecar and left the operator's process
    /// running. Clean stop (exit code 0); the session stays resumable.
    /// </summary>
    HerdrDetached = 9,

    /// <summary>
    /// CARD-0383: herdr never detected the expected kind and the pane was an idle shell, so
    /// KillAsync kept it as last-pane for in-place relaunch. Failed (the launch did not start);
    /// not a foreign-process <see cref="HerdrPaneLeftOpen"/> — do not tidy the pane by hand.
    /// Maps from <c>HerdrExitReasons.LaunchDetectTimeout</c> by name.
    /// </summary>
    HerdrLaunchDetectTimeout = 10
}

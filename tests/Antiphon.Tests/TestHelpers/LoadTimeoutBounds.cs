namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Real-time caps for waits that must succeed when a class starts its rows together.
/// A hook that never runs still fails when the cap elapses. None of these is infinite.
/// </summary>
public static class LoadTimeoutBounds
{
    /// <summary>
    /// <c>WaitCommand</c> bound for a wait that must observe a terminal executor.
    /// Was 1s. 10s covers a queued poll in the 11–17 row cleanup classes.
    /// Deliberate <c>StillRunning</c> checks keep their short bounds.
    /// </summary>
    public static readonly TimeSpan CheckpointPositiveWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Launch-queue idle bound. Was 15s. WorktreeRetirementRaceTests costs 119s across
    /// 54 rows (2.20s mean). At 39-wide fan-out, class p95 is about 3.5× the mean, so
    /// 3×15s = 45s (under 3.5×15s = 52.5s).
    /// </summary>
    public static readonly TimeSpan LaunchQueueIdleWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Idle bound for the phone-home restart scan. The row used to pass a 3s budget
    /// into <c>WaitForIdleAsync</c>. Twenty-one serial Linux runs alone peaked at
    /// 1813ms (p95 621ms). Twenty-one serial runs under eight sha256sum burners
    /// peaked at 3589ms (p95 791ms). That 3589ms sample is past 3s. Twice the burner
    /// maximum is 8s; twice the burner p95 is under the sample that missed 3s.
    /// </summary>
    public static readonly TimeSpan RestartScanIdleWait = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Phone-home signal bound. Was 5s. Measured whole-test passes were 7-19s,
    /// so 20s covers that latency. A longer cap only slows the known Finite_stall red.
    /// </summary>
    public static readonly TimeSpan PhoneHomeSignalWait = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Success bound for <c>SessionRuntimeActionTests</c>. The page yield and the
    /// other hooks that must run were 5s or 10s. Five Linux runs of the 28 rows
    /// under eight CPU burners peaked at 33.953s (poison-page). 2× that max is
    /// 68s; 70s accepts a page yield 20s late. A hook that never runs fails when
    /// this cap elapses.
    /// </summary>
    public static readonly TimeSpan SessionRuntimeActionWait = TimeSpan.FromSeconds(70);
}

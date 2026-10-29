namespace Antiphon.LandGraphAudit;

/// <summary>
/// One MSBuild pass is the timed unit. The audit runs two passes plus a canary copy.
/// A single 120s wait around that sum refused rc qualification 28bbb58e after the
/// first pass had already printed its not-built lines. Timeouts still refuse (exit 2).
/// </summary>
internal static class EvaluationBudget
{
    // server2 Linux, 24 CPUs, HEAD 8004259c, Antiphon.sln, 2026-10-29.
    // Unloaded wall ms: 31730, 29499, 26271.
    // Wall ms with 8 CPU burners: 26551, 34593, 34761 (nearest-rank p95 34761, 2x = 70s).
    // Loaded time to the first not-built line (expression scan + pass 1): 11848, 19267, 22328.
    // Nearest-rank p95 for n=3 is rank ceil(0.95*n)=3, so 22328. Twice that is 45s.
    // Loaded time after that line (copy + pass 2): 14703, 12433, 15326.
    public const int MeasuredLoadedPassP95Milliseconds = 22328;

    // No isolated copy stopwatch. 8000ms sits under every loaded second-phase sample,
    // which still includes pass 2, so it is a provisional upper bound for the copy
    // (small sample, Linux only). Hardlinks keep the copy on the metadata path.
    public const int MeasuredCopyP95Milliseconds = 8000;

    // Run 28bbb58e finished one pass inside the old combined ceiling and then refused.
    // Windows keeps that 120s as a per-pass floor until a Windows pass duration is recorded.
    public const int WindowsPassFloorSeconds = 120;

    public const int Passes = 2;

    // SDK discovery sits outside the phase clocks. The process ceiling has to outlive them.
    public const int StartupGraceSeconds = 60;

    public static int PassSeconds(int measuredPassP95Milliseconds, bool windows)
    {
        var twice = TwiceSeconds(measuredPassP95Milliseconds);
        return windows ? Math.Max(twice, WindowsPassFloorSeconds) : twice;
    }

    public static int CopySeconds(int measuredCopyP95Milliseconds) =>
        TwiceSeconds(measuredCopyP95Milliseconds);

    public static int TotalSeconds(int passSeconds, int passes, int copySeconds)
    {
        if (passes < 1) throw new ArgumentOutOfRangeException(nameof(passes));
        if (passSeconds < 1 || copySeconds < 1) throw new ArgumentOutOfRangeException(nameof(passSeconds));
        return passSeconds * passes + copySeconds;
    }

    public static int ToolCeilingSeconds =>
        TotalSeconds(
            PassSeconds(MeasuredLoadedPassP95Milliseconds, OperatingSystem.IsWindows()),
            Passes,
            CopySeconds(MeasuredCopyP95Milliseconds)) + StartupGraceSeconds;

    public static TimeSpan PassTimeout => TimeSpan.FromSeconds(
        PassSeconds(MeasuredLoadedPassP95Milliseconds, OperatingSystem.IsWindows()));

    public static TimeSpan CopyTimeout => TimeSpan.FromSeconds(CopySeconds(MeasuredCopyP95Milliseconds));

    private static int TwiceSeconds(int measuredP95Milliseconds)
    {
        if (measuredP95Milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(measuredP95Milliseconds));
        var seconds = (int)(((long)measuredP95Milliseconds * 2 + 999) / 1000);
        return Math.Max(1, seconds);
    }
}

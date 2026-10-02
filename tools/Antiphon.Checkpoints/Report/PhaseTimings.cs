namespace Antiphon.Checkpoints;

public sealed class PhaseTimings
{
    public double? SlotWaitSeconds { get; set; }
    public double? BuildSlotWaitSeconds { get; set; }
    public double? RowSlotWaitSeconds { get; set; }
    public double? BuildSeconds { get; set; }
    public string? ProducingBuild { get; set; }
    public double? StartupSeconds { get; set; }
    public double? TestsWallSeconds { get; set; }
    public double? TeardownSeconds { get; set; }
    public double? TestHostWallSeconds { get; set; }
    public string Method { get; set; } = "utc-trx-and-monotonic-host";
    public string? UnavailableReason { get; set; }

    public static PhaseTimings Reduce(DateTimeOffset launched, DateTimeOffset exited,
        TimeSpan monotonicElapsed, IReadOnlyList<TrxCaseResult> cases)
    {
        var value = new PhaseTimings();
        if (cases.Count == 0 || cases.Any(item => item.StartedAt is null || item.EndedAt is null))
            return Missing(value, "trx_timestamps_missing");
        if (monotonicElapsed < TimeSpan.Zero || exited < launched
            || Math.Abs((exited - launched - monotonicElapsed).TotalSeconds) > 2)
            return Missing(value, "host_clock_discontinuity");
        var first = cases.Min(item => item.StartedAt!.Value);
        var last = cases.Max(item => item.EndedAt!.Value);
        if (cases.Any(item => item.EndedAt < item.StartedAt) || first < launched || last > exited || first > last)
            return Missing(value, "trx_boundary_inconsistent");
        value.StartupSeconds = (first - launched).TotalSeconds;
        value.TestsWallSeconds = (last - first).TotalSeconds;
        value.TeardownSeconds = (exited - last).TotalSeconds;
        value.TestHostWallSeconds = monotonicElapsed.TotalSeconds;
        return value;
    }

    private static PhaseTimings Missing(PhaseTimings value, string reason)
    {
        value.UnavailableReason = reason;
        return value;
    }
}

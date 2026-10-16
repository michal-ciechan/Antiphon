using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed class TimingMeasurement
{
    public double? HostWallSeconds { get; init; }
    public double? CaseWindowSeconds { get; init; }
    public double? CaseDurationSumSeconds { get; init; }
    public double? StartupSeconds { get; init; }
    public double? TeardownSeconds { get; init; }
    public string? UnavailableReason { get; init; }
    public string MeasurementKind { get; init; } = "inclusive-case-window";
}

public sealed class TimingDiscoveryCase
{
    public string ExpandedId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Categories { get; set; } = [];
}

public sealed class TimingDiscoveryDocument
{
    public string AssemblyHash { get; set; } = "";
    public List<TimingDiscoveryCase> Cases { get; set; } = [];
}

public sealed class TimingCategoryJoin
{
    public bool Accepted { get; init; }
    public string? Reason { get; init; }
    public List<TimingDiscoveryCase> Cases { get; init; } = [];

    public static TimingCategoryJoin Refused(string reason) => new() { Accepted = false, Reason = reason };
}

public static class TimingMeasurements
{
    public static TimingMeasurement Measure(DateTimeOffset launched, DateTimeOffset exited, TimeSpan monotonic, IReadOnlyList<TrxCaseResult> cases)
    {
        var sum = cases.Sum(item => item.DurationSeconds);
        if (cases.Count == 0 || cases.Any(item => item.StartedAt is null || item.EndedAt is null))
            return Unknown("trx_timestamps_missing");
        if (monotonic < TimeSpan.Zero || exited < launched
            || Math.Abs((exited - launched - monotonic).TotalSeconds) > 2)
            return Unknown("host_clock_discontinuity");
        var first = cases.Min(item => item.StartedAt!.Value);
        var last = cases.Max(item => item.EndedAt!.Value);
        if (cases.Any(item => item.EndedAt < item.StartedAt) || first < launched || last > exited || first > last)
            return Unknown("trx_boundary_inconsistent");
        return new TimingMeasurement
        {
            HostWallSeconds = monotonic.TotalSeconds,
            CaseWindowSeconds = (last - first).TotalSeconds,
            CaseDurationSumSeconds = sum,
            StartupSeconds = (first - launched).TotalSeconds,
            TeardownSeconds = (exited - last).TotalSeconds,
        };
    }

    public static TimingCategoryJoin Join(TimingDiscoveryDocument document, string producingAssemblyHash)
    {
        if (!string.Equals(document.AssemblyHash, producingAssemblyHash, StringComparison.Ordinal))
            return TimingCategoryJoin.Refused("category-build-bound");
        return new TimingCategoryJoin
        {
            Accepted = true,
            Cases = document.Cases
                .GroupBy(item => item.ExpandedId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
        };
    }

    public static TimingDiscoveryDocument ParseDiscovery(string json) =>
        JsonSerializer.Deserialize<TimingDiscoveryDocument>(json, TimingJournal.Json) ?? new TimingDiscoveryDocument();

    private static TimingMeasurement Unknown(string reason) => new()
    {
        UnavailableReason = reason,
        MeasurementKind = "unknown",
    };
}

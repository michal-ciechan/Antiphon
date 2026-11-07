namespace Antiphon.SessionRunner.Contracts;

/// <summary>Durable identity: retries preserve all four fields; sequence is positive within a boot.</summary>
public sealed record HostLoadSource(string RunnerId, Guid RunnerStoreId, Guid BootId, long Sequence);
public sealed record HostLoadMemory(long? TotalBytes, long? AvailableBytes, long? UsedBytes);
public sealed record HostLoadSwap(long? TotalBytes, long? FreeBytes, long? UsedBytes, string Meaning);
public sealed record HostLoadVolume(string Path, long? FreeBytes, long? TotalBytes);
public sealed record HostLoadProcess(string Name, Guid? SessionId, double? CpuPercent, long? WorkingSetBytes);

/// <summary>Observation only. This object never acquires, renews, releases or changes admission.</summary>
public sealed record HostAdmissionSnapshot(
    string? Id, string? PhysicalHostKey, long? Revision, DateTimeOffset? ObservedAt,
    string State, string Mode, int? Occupied, int? Budget, int? Waiters,
    int? ActiveCommands = null, int? HardCeiling = null)
{
    public static HostAdmissionSnapshot Unknown(string? host = null) =>
        new(null, host, null, null, "unknown", "unknown", null, null, null);
}

/// <summary>Independent additive v1 wire format; the legacy hostStatsV1 DTO is unchanged.</summary>
public sealed record HostLoadSample
{
    public int SchemaVersion { get; init; } = 1;
    public required HostLoadSource Source { get; init; }
    public DateTimeOffset SampledAt { get; init; }
    public int IntervalSeconds { get; init; } = 5;
    public string? PhysicalHostKey { get; init; }
    public long? BindingRevision { get; init; }
    public Guid? SourceEpoch { get; init; }
    public string MetricScope { get; init; } = "unknown";
    public int? HostLogicalCores { get; init; }
    public double? EffectiveCoreLimit { get; init; }
    public double? CpuPercent { get; init; }
    public double? BusyCoreEquivalent { get; init; }
    public double? IoWaitPercent { get; init; }
    public double? Load1 { get; init; }
    public double? Load5 { get; init; }
    public double? Load15 { get; init; }
    public double? LoadPerCore { get; init; }
    public HostLoadMemory Memory { get; init; } = new(null, null, null);
    public HostLoadSwap Swap { get; init; } = new(null, null, null, "swap");
    public IReadOnlyList<HostLoadVolume> Volumes { get; init; } = [];
    public int? ProcessCount { get; init; }
    public IReadOnlyList<HostLoadProcess> Processes { get; init; } = [];
    public IReadOnlyDictionary<string, string> UnknownReasons { get; init; } = new Dictionary<string, string>();
    public HostAdmissionSnapshot Authority { get; init; } = HostAdmissionSnapshot.Unknown();
}

public sealed record HostLoadGap(Guid RunnerStoreId, Guid BootId, long FromSequence, long ToSequence, string Reason);
public sealed record HostLoadHistoryPage(int SchemaVersion, HostLoadSource Source,
    IReadOnlyList<HostLoadSample> Samples, IReadOnlyList<HostLoadGap> Gaps,
    string? NextCursor, bool More, bool Complete);
public sealed record PhoneHomeHostLoadHistoryRequest(string? After = null, int Limit = 120);
public sealed record RunnerLoadShadowDto(DateTimeOffset ObservedAt, bool Shadow, double? CommandPressure,
    double? DispatchPressure, string Command, string Dispatch, int RecommendedAdditionalTasks);

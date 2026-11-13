namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0718 D-9: <c>SessionRunner:HostStats</c>. <see cref="Enabled"/> false answers
/// <c>/host-stats</c> 404 and the phone-home operations unsupported.
/// </summary>
public sealed class HostStatsSettings
{
    public const string SectionName = "SessionRunner:HostStats";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the sampler reads the host. The ring stores one slot per whole second of this.</summary>
    public int IntervalMs { get; set; } = 5000;

    /// <summary>How often the top-consumer scan runs. The sampler tick only reads the latest result.</summary>
    public int TopConsumerScanIntervalMs { get; set; } = 5000;

    public int RetentionMinutes { get; set; } = 30;

    /// <summary>Per-process CPU and working set for the runner and live session pids.</summary>
    public bool ProcessSampling { get; set; } = true;

    /// <summary>
    /// Volumes to stat. Empty until bound: registration fills cwd and <c>SessionLogPath</c>.
    /// </summary>
    public string[] Volumes { get; set; } = [];

    public bool HistoryEnabled { get; set; } = true;
    public string? HistoryPath { get; set; }
    public string RunnerId { get; set; } = "local";
    public string? PhysicalHostKey { get; set; }
    public long? BindingRevision { get; set; }
    public Guid? SourceEpoch { get; set; }
    public string? AuthorityEndpoint { get; set; }
    public string? AuthorityId { get; set; }
    public long SpoolMaxBytes { get; set; } = 256L * 1024 * 1024;
    public int SpoolMaxHours { get; set; } = 24;
    public int AppendQueueCapacity { get; set; } = 48;

    public void Validate()
    {
        if (IntervalMs < 1000)
            throw new InvalidOperationException($"{SectionName}:IntervalMs must be at least 1000.");
        if (TopConsumerScanIntervalMs is < 1000 or > 60_000)
            throw new InvalidOperationException($"{SectionName}:TopConsumerScanIntervalMs must be from 1000 through 60000.");
        if (RetentionMinutes < 1)
            throw new InvalidOperationException($"{SectionName}:RetentionMinutes must be at least 1.");
        if (HistoryPath is not null && string.IsNullOrWhiteSpace(HistoryPath))
            throw new InvalidOperationException($"{SectionName}:HistoryPath must be absent or a nonempty path.");
        if (SpoolMaxBytes < 4096 || SpoolMaxBytes > 256L * 1024 * 1024 || SpoolMaxHours is < 1 or > 24
            || AppendQueueCapacity is < 1 or > 120)
            throw new InvalidOperationException($"{SectionName}: history bounds are invalid.");
        if (AuthorityEndpoint is not null && (!Uri.TryCreate(AuthorityEndpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidOperationException($"{SectionName}:AuthorityEndpoint must be an http(s) authority without credentials.");
    }
}

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

    public int RetentionMinutes { get; set; } = 30;

    /// <summary>Per-process CPU and working set for the runner and live session pids.</summary>
    public bool ProcessSampling { get; set; } = true;

    /// <summary>
    /// Volumes to stat. Empty until bound: registration fills cwd and <c>SessionLogPath</c>.
    /// A single comma-separated value (the server2 compose env var) is split.
    /// </summary>
    public string[] Volumes { get; set; } = [];

    public void Validate()
    {
        if (IntervalMs < 1000)
            throw new InvalidOperationException($"{SectionName}:IntervalMs must be at least 1000.");
        if (RetentionMinutes < 1)
            throw new InvalidOperationException($"{SectionName}:RetentionMinutes must be at least 1.");
    }
}

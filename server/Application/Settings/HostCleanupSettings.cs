namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0826. Execute remains off; report success never changes this setting.</summary>
public sealed class HostCleanupSettings
{
    public bool Enabled { get; set; }
    public bool Execute { get; set; }
    public string TimeZoneId { get; set; } = "Europe/London";
    public int MaxAttempts { get; set; } = 10;
    public long MaxBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public int MaxCandidates { get; set; } = 10_000;
    public int MaxDescendants { get; set; } = 100_000;
    public int PassSeconds { get; set; } = 300;
    public long NamespaceBudgetBytes { get; set; } = 100L * 1024 * 1024 * 1024;
    public double WarningFraction { get; set; } = 0.6;
    public double CriticalFraction { get; set; } = 0.9;
    public long MinimumFreeBytes { get; set; } = 50L * 1024 * 1024 * 1024;
    public double MinimumFreeFraction { get; set; } = 0.1;
    public long BacklogBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public int BacklogDays { get; set; } = 3;
    public int SampleFreshMinutes { get; set; } = 30;
    public int ReportRetentionDays { get; set; } = 30;
    public int PageSize { get; set; } = 50;
}

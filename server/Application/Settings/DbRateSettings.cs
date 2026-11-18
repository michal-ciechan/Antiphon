namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-1987. Defaults live in code; nothing in configuration is required.</summary>
public sealed class DbRateSettings
{
    public const string SectionName = "DbRateSampler";

    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between samples. Values outside 1..3600 stay on the 60 second default.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Ring length in minutes. Values outside 1..1440 stay on 24 hours.</summary>
    public int RetentionMinutes { get; set; } = 1440;

    public int IntervalSecondsOrDefault => IntervalSeconds is >= 1 and <= 3600 ? IntervalSeconds : 60;

    public int RetentionOrDefault => RetentionMinutes is >= 1 and <= 1440 ? RetentionMinutes : 1440;
}

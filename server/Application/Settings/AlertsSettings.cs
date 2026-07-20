namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Alert routing (spec part B, Q6 decision): a HARD per-sink send window — at most one channel
/// message per sink per <see cref="MinMinutesBetweenSends"/>; everything raised inside the window
/// accumulates into one grouped, deduped digest.
/// </summary>
public sealed class AlertsSettings
{
    public bool RoutingEnabled { get; set; } = true;

    /// <summary>The hard send window per sink (Q6: "max 1 message every 5 minutes or so").</summary>
    public int MinMinutesBetweenSends { get; set; } = 5;

    /// <summary>How often pending digests are checked for flushing.</summary>
    public int FlushTickSeconds { get; set; } = 15;

    /// <summary>When true, Critical alerts flush immediately, bypassing the window (default off per Q6).</summary>
    public bool CriticalBypassWindow { get; set; }

    public int AlertRetentionDays { get; set; } = 30;
}

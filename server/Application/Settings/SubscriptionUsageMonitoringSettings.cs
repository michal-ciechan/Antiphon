namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0143: periodic idle poll of a provider's TUI usage panel. Defaults are the off state —
/// a pay-as-you-go / work-machine deployment has no subscription tier, and polling there is
/// meaningless or misdirected. No <c>appsettings.json</c> entry is required.
/// </summary>
public sealed class SubscriptionUsageMonitoringSettings
{
    /// <summary>The whole feature gate. Default false.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hosted-service cadence in minutes. Card's stated 30.</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>
    /// When false (default), only kinds whose poll contract is Supported are eligible.
    /// Grok is Degraded until S5 measures tab navigation and percentage polarity.
    /// </summary>
    public bool IncludeDegradedProviders { get; set; }

    /// <summary>
    /// Per-session floor, checked against both the in-memory stamp and the newest stored
    /// sample, so a restart storm cannot re-poll on every boot.
    /// </summary>
    public int MinPollIntervalMinutes { get; set; } = 25;

    /// <summary>Bounds a pass; anything dropped is logged by count.</summary>
    public int MaxSessionsPerSweep { get; set; } = 10;

    /// <summary>Hard budget per poll.</summary>
    public int PerSessionTimeoutSeconds { get; set; } = 20;

    /// <summary>Wait for the panel to render after Enter.</summary>
    public int PanelTimeoutSeconds { get; set; } = 5;

    /// <summary>Settle after Esc / after a navigation key.</summary>
    public int OverlaySettleMs { get; set; } = 400;

    /// <summary>
    /// Consecutive failed polls for one session before one deduped Warning incident.
    /// Never Critical — a failed usage poll is a missing convenience, not a broken agent.
    /// </summary>
    public int ConsecutiveFailuresBeforeIncident { get; set; } = 3;
}

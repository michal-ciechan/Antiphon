namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Always-on agent supervision (spec: 2026-07-20-always-on-agents-and-alerting.md).
/// The backoff ladder never gives up: min(BaseSeconds · 2ⁿ, BackoffMaxSeconds) — with the
/// defaults that is 5s, 10s, … ~15 min, ~2 h, ~15 h, days, capped at 30 days forever.
/// </summary>
public sealed class SupervisionSettings
{
    public bool Enabled { get; set; } = true;

    public int TickSeconds { get; set; } = 10;

    public double BackoffBaseSeconds { get; set; } = 5;

    /// <summary>30 days — the ladder's cap; retries continue at this cadence indefinitely.</summary>
    public double BackoffMaxSeconds { get; set; } = 2_592_000;

    /// <summary>Continuous Running time after which the failure counter resets.</summary>
    public int HealthyUptimeResetMinutes { get; set; } = 10;

    /// <summary>Consecutive failures after which restarts use a fresh conversation instead of resume.</summary>
    public int FreshAfterResumeFailures { get; set; } = 2;

    public int IncidentRetentionDays { get; set; } = 30;
    public int IncidentCapPerAgent { get; set; } = 500;
}

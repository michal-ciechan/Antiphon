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

    public RcWatchSettings RcWatch { get; set; } = new();
    public DeliveryVerificationSettings DeliveryVerification { get; set; } = new();
}

/// <summary>
/// Remote-control bridge watch. Thresholds calibrated 2026-07-20: an idle healthy session holds
/// 2-3 Anthropic connections continuously (never observed at zero across 57 consecutive samples),
/// so 5 consecutive zero-connection probes at 60s cadence (= 5 min of sustained absence, i.e.
/// "5-10 missed normal probes") is a confident dead verdict, never a blip.
/// </summary>
public sealed class RcWatchSettings
{
    public bool Enabled { get; set; } = true;
    public int ProbeIntervalSeconds { get; set; } = 60;

    /// <summary>Only repair sessions idle this long (no new output sequence).</summary>
    public int IdleQuietMinutes { get; set; } = 5;

    public int ConsecutiveFailedProbesBeforeAction { get; set; } = 5;
    public int ReArmAttemptsBeforeRestart { get; set; } = 2;

    /// <summary>How long after a re-arm before the bridge is probed again (arming takes seconds).</summary>
    public int ReArmSettleMinutes { get; set; } = 3;
}

/// <summary>
/// Delivery-time composer verification — the ONLY wedge/deadness detection (the periodic TUI echo
/// probe false-positive-killed healthy idle sessions on 2026-07-20, and the periodic round-trip
/// "pong" probe was removed 2026-07-23: sessions are only checked when a message we actually sent
/// misbehaves, never speculatively). When a message is delivered
/// to a Claude session the body is typed, then the rendered screen must show evidence of it
/// (<c>ComposerDeliveryEvidence</c> — tail/head fragment or a new paste placeholder, per the
/// ClaudeComposerRenderCanaryTests contract) BEFORE the submitting Enter is sent; after Enter the
/// output sequence must advance. On failure the message reverts to Pending, an incident is
/// recorded, and always-on agents get a session restart (the composer dies with the process, so
/// redelivery cannot double-type).
/// </summary>
public sealed class DeliveryVerificationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long typed text may take to show up on the rendered screen. Generous on purpose:
    /// the echo probe's 750ms settle window is what false-positived on real TUIs.
    /// </summary>
    public int EvidenceTimeoutSeconds { get; set; } = 15;

    public int PollIntervalMs { get; set; } = 500;

    /// <summary>
    /// After the submitting Enter, the output sequence must advance within this window
    /// (a real submit redraws the screen immediately; this is wedge detection, not
    /// reply detection).
    /// </summary>
    public int PostSubmitAdvanceTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Stranded-queue watchdog: pending messages older than this on an IDLE always-on session are
    /// re-flushed (covers redelivery after a verification-failure restart and missed turn-ends).
    /// </summary>
    public int StrandedAgeSeconds { get; set; } = 60;
}

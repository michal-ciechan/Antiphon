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
    public LivenessProbeSettings LivenessProbe { get; set; } = new();
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

public sealed class LivenessProbeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// TUI echo probe (type + verify screen delta + backspace; free, no tokens). 0 = DISABLED —
    /// the current default: on 2026-07-20 the probe false-positive-killed healthy idle sessions
    /// (typed char produced no rendered-screen delta within the settle window on real Claude
    /// TUIs), each kill costing a session generation. Re-enable only after the screen-delta
    /// detection is fixed and proven against a live idle session.
    /// </summary>
    public int TuiEchoIntervalMinutes { get; set; }

    /// <summary>Round-trip probe (queued healthcheck prompt; costs a model turn).</summary>
    public int RoundTripIntervalHours { get; set; } = 6;

    /// <summary>Round-trip verdict window: output must advance within this after the enqueue.</summary>
    public int RoundTripTimeoutMinutes { get; set; } = 10;
}

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
/// prompt must become a <c>UserPrompt</c> transcript record carrying our body (CARD-0055 — the
/// output sequence merely advancing is NOT delivery, it is a redraw). On failure the message
/// reverts to Pending, an incident is recorded, and always-on agents get a session restart (the
/// composer dies with the process, so redelivery cannot double-type).
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

    /// <summary>
    /// CARD-0055 kill switch. With it off, a delivery is Delivered as soon as the output sequence
    /// advances after Enter — which any redraw satisfies, including the composer re-rendering the
    /// text it is STILL HOLDING. That is how a note sat unsubmitted for 104 minutes and how a
    /// second note was marked Sent while its Enter submitted the previous, stale body.
    /// </summary>
    public bool TranscriptConfirmEnabled { get; set; } = true;

    /// <summary>
    /// How long a submitted prompt has to become a <c>UserPrompt</c> transcript record. The tailer
    /// polls at 300 ms and the runner event pump is sub-second, so this is ~30x margin. Held under
    /// the per-session queue lock on purpose: serialization is what makes the Enter re-press safe
    /// (nothing else can put a different body in the composer). Lower it if the tail proves noisy —
    /// do NOT release the lock mid-confirm.
    /// </summary>
    public int TranscriptConfirmTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How long to wait for the record before pressing Enter again. Long enough that a slow but
    /// SUCCESSFUL submit's record usually lands first, so most confirmations cost one Enter.
    /// </summary>
    public int ReEnterIntervalSeconds { get; set; } = 7;

    /// <summary>
    /// Total Enters per delivery, including the first (matches <c>VerifiedSubmitOptions</c>). The
    /// retry is ENTER-ONLY and never a re-type: if the first Enter did submit, the composer is
    /// empty and Enter on an empty composer is a no-op. Nothing here may ever re-type a body that
    /// might already have gone in.
    /// </summary>
    public int SubmitAttempts { get; set; } = 3;

    /// <summary>
    /// Grace window applied AFTER a <c>NoTranscriptRecord</c> verdict and BEFORE anything
    /// destructive happens, during which the same text matcher keeps looking for the record.
    ///
    /// <para>The confirm window expiring proves our own ingestion had not caught up, not that the
    /// submit failed. On session <c>22e0df09</c> (2026-08-16) the record landed 0.8s after the
    /// verdict: the always-on kill had already destroyed a session that had taken the message
    /// correctly, and the existing late-confirm ran on the corpse and marked it Sent. A brand-new
    /// session is where this bites hardest — its transcript file does not exist until the first
    /// submit creates it, and discovery + binding + first ingestion all land inside the window.</para>
    ///
    /// <para>Text match only, same as <c>LateConfirmAttemptedMessagesAsync</c>: this can only turn
    /// a failure into a success on positive evidence that our exact body is in the transcript.</para>
    ///
    /// <para>The window is short because it is NOT a waiting game: each iteration PULLS the
    /// runner's transcript (<c>AgentSessionRuntime.CatchUpTranscriptAsync</c>) rather than hoping
    /// the live stream catches up. That distinction is measured, not assumed. On session
    /// <c>e809ce65</c> the interpretation brief's <c>UserPrompt</c> was written by Claude 0.9s
    /// after the submit and stored by us <b>45 seconds later</b>, in one burst with five other
    /// records, at the exact moment the session was killed — the kill is what produced the
    /// evidence that the kill was wrong. A pure wait cannot win that race: a 90s window was tried
    /// on session <c>5536ae88</c> and still lost by the same 1.2s, because the flush is triggered
    /// by the session ENDING. The same stall blinds <c>IsWorkingAsync</c> (it reads the same rows),
    /// which is why the working-kill guard reported <c>working=false</c> about a session that was
    /// visibly mid-turn on screen — both brakes share one dependency, and the pull fixes both,
    /// since <c>working</c> is recomputed after this window.</para>
    /// </summary>
    public int PostFailureConfirmGraceSeconds { get; set; } = 20;

    /// <summary>
    /// How many times a queued message may be typed into a terminal before it PARKS for a human
    /// (CARD-0055). A parked message stays Pending and visible in the queue UI, where cancel and
    /// re-enqueue already exist, but no automatic path picks it up again — an unbounded retry loop
    /// against a terminal that keeps eating Enters is how the same body reaches an agent five times.
    /// Parking raises an incident, Critical when the agent is channel-bound: a parked channel reply
    /// is a human waiting on a dead line.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>
    /// Total attempts at a BOOT prompt — the launch-time writes (<c>/remote-control</c>,
    /// <c>/rename</c>, a card's work prompt) that run before the queue exists (CARD-0056).
    ///
    /// <para>Unlike <see cref="SubmitAttempts"/>, which is Enter-only, this re-types the whole
    /// verified submit. That is safe here and ONLY here: the attempt failed with a
    /// <c>PromptDeliveryException</c>, which means no composer evidence appeared — the same check
    /// that would gate an Enter says the composer does not hold the body, so typing it again cannot
    /// double-submit. CARD-0055's never-re-type rule governs the phase AFTER evidence; this is the
    /// phase before it.</para>
    ///
    /// <para>Retrying is the fix the evidence actually supports. On 2026-08-16 a 15-character
    /// <c>/remote-control</c> was typed into a healthy orchestrator mid-resume-render and never
    /// reached its composer; the supervisor's replacement, resuming the SAME conversation 60
    /// seconds later, armed on its first try. Raising <see cref="EvidenceTimeoutSeconds"/> is
    /// refuted by that capture and stays at 15: no poll duration can reveal text that was never
    /// buffered. 3 × 15 s ≈ 45 s of typing spread over ~49 s also outlasts any history render
    /// measured so far.</para>
    /// </summary>
    public int BootPromptAttempts { get; set; } = 3;

    /// <summary>
    /// Quiet period between boot-prompt attempts. The demonstrated race is type-at-ready vs
    /// resume-history-render, so the retry wants the TUI to have moved on, not to type faster.
    /// </summary>
    public int BootPromptRetryDelaySeconds { get; set; } = 2;
}

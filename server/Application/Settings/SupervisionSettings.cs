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
    public ApiErrorRecoverySettings ApiErrorRecovery { get; set; } = new();
    public HerdrCorroborationSettings HerdrCorroboration { get; set; } = new();
    public OrchestratorInvestigationSettings OrchestratorInvestigation { get; set; } = new();
    public AppHostWatchdogStateSettings AppHostWatchdogState { get; set; } = new();
    public QueuedInputWatchSettings QueuedInputWatch { get; set; } = new();
}

/// <summary>
/// CARD-0292 S4: the swallowed-input watchdog — a per-minute sweep for a live session whose
/// latest <c>QueueEnqueue</c> transcript row has no later conversion while the session reads
/// idle. Detection only: an incident + attention row, never a kill, keystroke, or Esc.
/// </summary>
public sealed class QueuedInputWatchSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How old the unconverted enqueue must be before the incident fires. A mid-turn session
    /// legitimately holds queued input for the length of the turn (the working gate covers that);
    /// this covers slow drains around a turn boundary.
    /// </summary>
    public int StuckMinutes { get; set; } = 3;

    /// <summary>
    /// Re-raise the incident as Error (Critical when the agent is channel-bound) once the same
    /// episode has been stuck this long — the <c>TaskProgressStalled</c> ladder shape.
    /// </summary>
    public int EscalateToErrorAfterMinutes { get; set; } = 15;
}

/// <summary>
/// CARD-0247 S3: periodic detection of orchestrator investigation runs. Detection only —
/// Warning incident + Process-group attention row; never kills or retypes.
/// </summary>
public sealed class OrchestratorInvestigationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often the supervisor tick piggy-backs the investigation sweep.</summary>
    public int SweepPeriodSeconds { get; set; } = 60;
}

/// <summary>
/// CARD-0245 S1: read the independent watchdog-state observer document and raise Critical
/// attention when the recovery mechanism is Disabled/Missing/Unknown outside maintenance.
/// Detection only — never re-enables the Scheduled Task and never restarts AppHost.
/// </summary>
public sealed class AppHostWatchdogStateSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to <c>logs/apphost-watchdog-state.json</c>. Empty = walk up from the content root
    /// looking for <c>Antiphon.sln</c>, then that repo's logs file.
    /// </summary>
    public string StateDocumentPath { get; set; } = "";

    /// <summary>How often the hosted reader re-reads the observer document.</summary>
    public int PollSeconds { get; set; } = 30;
}

/// <summary>
/// CARD-0162: periodic corroboration of herdr agent_status vs transcript IsWorkingAsync.
/// Detection only — Warning incident row; never kills or retypes.
/// </summary>
public sealed class HerdrCorroborationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often the supervisor tick piggy-backs the corroboration sweep.</summary>
    public int SweepPeriodSeconds { get; set; } = 60;

    /// <summary>
    /// Raise only when the herdr status has been STABLE in the disagreeing value this long.
    /// Kills turn-end flap; real stuck conditions persist for hours (CARD-0047).
    /// </summary>
    public int MinSustainedMinutes { get; set; } = 10;
}

/// <summary>
/// Timed retry of a turn killed by an API-error stub (CARD-0072 S5a). A one-minute sweep against
/// a one-minute first rung means the first retry lands 1–2 minutes after the stub; against a
/// 31-minute silence that is acceptable — do not "fix" the ladder's first rung wondering why it
/// is late.
/// </summary>
public sealed class ApiErrorRecoverySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the supervisor tick piggy-backs the adopt+fire pass. Default 60s matches the
    /// CARD-0067 / CARD-0082 sweeps sitting next to it.
    /// </summary>
    public int SweepPeriodSeconds { get; set; } = 60;

    /// <summary>
    /// Only TurnEnd stubs this recent are adopted. Older deaths are left alone — the fields are
    /// not retroactive, and a 71k-row scan of history is not this sweep's job.
    /// </summary>
    public int AdoptWindowMinutes { get; set; } = 180;

    /// <summary>Unknown class: enqueue this many resumes, then park (§D8).</summary>
    public int UnknownAttemptCap { get; set; } = 3;

    /// <summary>
    /// Transient: the ladder runs hourly indefinitely, but a Warning incident (Critical when
    /// channel-bound) fires once total dead time crosses this threshold.
    /// </summary>
    public int DeadTimeWarningHours { get; set; } = 2;

    /// <summary>
    /// Consecutive Wall deaths on one session before the resume parks and escalates Critical.
    /// A 30-minute nudge at a five-hour quota wall then costs at most three cheap deliveries.
    /// </summary>
    public int WallDeathCap { get; set; } = 3;

    /// <summary>
    /// Fallback hold duration in hours for an AutoDetected wall whose source text does not
    /// state a reset (CARD-0335). Default 6. Malformed or non-positive configuration clamps to 1.
    /// </summary>
    public int ModelCapFallbackHoldHours { get; set; } = 6;

    /// <summary>Clamped <see cref="ModelCapFallbackHoldHours"/> used by recovery and the availability reader.</summary>
    public int EffectiveModelCapFallbackHoldHours =>
        ModelCapFallbackHoldHours > 0 ? ModelCapFallbackHoldHours : 1;

    /// <summary>
    /// Enqueued WhenIdle on a Transient / Unknown death. Same shape as
    /// <c>AgentSessionSettings.ResumeContinuePrompt</c>.
    /// </summary>
    public string TransientPrompt { get; set; } =
        "Your previous turn was killed by a transient API error. Review where you got to and "
        + "continue the work you were doing; if it was already complete, briefly confirm that instead.";

    /// <summary>
    /// Enqueued WhenIdle on a Wall death (degraded 30-minute rung until CARD-0022's parser).
    /// The commit-first sentence is spec §D6's answer to a dirty shared checkout: the session
    /// that made the mess still holds the context to attribute it.
    /// </summary>
    public string WallPrompt { get; set; } =
        "Your previous turn was killed by a usage-limit wall. Commit any in-progress work to a "
        + "branch first, then review where you got to and continue. If the work was already "
        + "complete, briefly confirm that instead.";
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
    /// After the composer first shows the typed body, require its output sequence to stay unchanged
    /// for this long before taking the post-submit baseline. This excludes the body's own trailing
    /// render frames from being mistaken for evidence that the following Enter submitted it.
    /// Bounded to three seconds by the delivery path.
    /// </summary>
    public int PostEvidenceSettleMs { get; set; } = 500;

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
    /// CARD-0164: wall-clock floor for the unobservable-baseline confirm loop (and the late-confirm
    /// null-baseline arm). Same shape as <c>AgentSessionService.BootConfirmClockTolerance</c>
    /// (CARD-0056): a <c>--resume</c>'s copied history and a late-binding tailer's backfill keep
    /// ORIGINAL timestamps while backfill rebases their sequences past any sequence floor — only
    /// the wall clock tells them from fresh evidence. A candidate row must have
    /// <c>Timestamp != null &amp;&amp; Timestamp &gt;= UtcNow − this</c>, captured before the body
    /// write. Default 30 s also covers <c>QueuedUserPrompt</c> rows whose stamp is the
    /// composer-enqueue time (at most the evidence window before Enter).
    /// </summary>
    public int UnobservableBaselineConfirmClockToleranceSeconds { get; set; } = 30;

    /// <summary>
    /// CARD-0340 S3 / CARD-0342. The stranded sweep recovers a <c>Sent</c> row with a null
    /// verdict (interrupted confirm) and a Pending <see cref="DeliveryVerdict.NoSubmitOutput"/>
    /// only when <c>LastDeliveryStartedAt</c> is inside this window, so pre-migration rows age
    /// out. Default 60 minutes.
    /// </summary>
    public int InterruptedAttemptWindowMinutes { get; set; } = 60;

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
    /// CARD-0103. How long after a message was ENQUEUED a <c>NoComposerEvidence</c> verdict on a
    /// session that has never produced a single transcript row is treated as "still becoming
    /// input-responsive" rather than as a spent attempt.
    ///
    /// <para>The budget arithmetic is the defect this repairs: 3 attempts on a ~60s watchdog cadence
    /// is ~2.5 minutes, and the measured dead zone in which a painted Claude TUI is not yet draining
    /// stdin ran 48-200 seconds (2026-08-20). The whole retry budget was therefore spendable INSIDE
    /// one stall, after which the message parked silently in a session everyone believed was healthy
    /// and the dispatcher failed the task at 10 minutes with "Boot prompt was never delivered."</para>
    ///
    /// <para>Inside this window the attempt is REFUNDED (see
    /// <c>SessionMessageQueueService.HandleDeliveryFailureAsync</c>), the always-on fresh-composer
    /// kill is withheld — killing and relaunching a session that is merely still waking restarts the
    /// same race, which is CARD-0047's restart-loop shape — and the incident drops to a single
    /// Warning instead of one Error per attempt. Retries then ride the 60s stranded sweep, so a
    /// pre-first-turn message gets roughly 8 chances inside the dispatcher's watchdog instead of 3.</para>
    ///
    /// <para>8 minutes, deliberately INSIDE the dispatcher's 10-minute <c>FailNeverStartedAsync</c>
    /// clock: a genuinely dead session still charges its last two attempts and still fails loudly on
    /// schedule. This changes what an attempt MEANS pre-first-turn; it does not raise
    /// <see cref="MaxDeliveryAttempts"/>, and every <c>DeliveryAttempts &lt; MaxDeliveryAttempts</c>
    /// predicate keeps its existing meaning because the counter itself is kept honest.</para>
    ///
    /// <para>Scope is the triple condition and nothing wider: the verdict must be
    /// <c>NoComposerEvidence</c> (the Enter was never sent, so nothing can have been submitted — the
    /// one verdict where not charging is provably safe), the attempt's stamped baseline must be
    /// unobservable (<c>LastDeliveryBaselineSequence == null</c>, i.e. zero transcript rows at type
    /// time), and the message must be younger than this. A session that started working and THEN
    /// stalled has a non-null baseline and is left entirely to CARD-0055's original design.</para>
    /// </summary>
    public int PreFirstTurnNoEvidenceGraceMinutes { get; set; } = 8;

    /// <summary>
    /// CARD-0137 S5. One-shot Esc-and-retype on <c>NoComposerEvidence</c> when the kind's
    /// <c>TerminalOverlay</c> is Supported, the session is idle after a fresh transcript pull,
    /// and Enter has not been sent. Default on: the recovery is gated on those facts, not on
    /// this flag. Off is the kill switch if a TUI upgrade makes Esc unsafe.
    /// </summary>
    public bool OverlayRecoveryEnabled { get; set; } = true;

    /// <summary>
    /// Settle after an overlay dismiss key before re-snapshotting / re-typing. Shared with the
    /// poll transport's default (400).
    /// </summary>
    public int OverlaySettleMs { get; set; } = 400;

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

/// <summary>
/// CARD-0101: when a CARD-0006 transcript-bind refusal stops being "a session is degraded" and
/// becomes "a session has been unreadable for hours and nobody has been told".
///
/// The refusal itself is correct and must keep repeating — it is the only signal that the session
/// is still unbound — but at Warning, forever, on a five-minute cadence, it is indistinguishable
/// from noise. On 2026-08-20 six sessions carried 32-73 identical Warning incidents each, up to 3h6m
/// of continuous refusal, and none of it reached a human: the existing Critical path fires only for
/// channel-bound agents, and a delegate task agent is never channel-bound.
/// </summary>
public sealed class TranscriptBindingSettings
{
    /// <summary>
    /// Continuous refusal past this raises <c>AgentIncidentKind.TranscriptBindStuck</c> at Critical,
    /// channel binding or not. Well past the minutes a slow first turn can legitimately take, well
    /// under the hours the 2026-08-20 cascade ran for.
    /// </summary>
    public int StuckAfterMinutes { get; set; } = 30;

    /// <summary>
    /// How often the escalated incident may re-fire while the fault continues. The underlying
    /// <c>TranscriptBindFailed</c> rows keep their own five-minute cadence untouched; this is the
    /// loud one, and it is deliberately much quieter so it stays readable as an escalation.
    /// </summary>
    public int StuckRepeatMinutes { get; set; } = 60;

    /// <summary>Kill switch for the escalation only. The base incident is unaffected.</summary>
    public bool EscalationEnabled { get; set; } = true;
}

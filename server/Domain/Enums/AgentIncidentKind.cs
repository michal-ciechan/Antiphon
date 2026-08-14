namespace Antiphon.Server.Domain.Enums;

/// <summary>What happened to a supervised agent. Append-only audit; feeds UI + alerts.</summary>
public enum AgentIncidentKind
{
    /// <summary>The agent's session died unexpectedly (non-zero exit / vanished).</summary>
    Crash = 0,

    /// <summary>A supervised start attempt threw.</summary>
    StartFailure = 1,

    /// <summary>A restart was scheduled (records attempt #, delay, and absolute next-retry time).</summary>
    RestartScheduled = 2,

    /// <summary>The agent ran healthily long enough to reset the backoff ladder.</summary>
    Recovered = 3,

    /// <summary>The backoff ladder crossed a tier boundary (hourly = Warning, daily = Critical).</summary>
    BackoffEscalated = 4,

    /// <summary>A human stopped an always-on agent; supervision is suspended until a manual start.</summary>
    SuspendedByUser = 5,

    /// <summary>A start cleared the suspension/backoff state.</summary>
    ResumedByUser = 6,

    /// <summary>Remote-control bridge found degraded (slice 3).</summary>
    RcDegraded = 7,

    /// <summary>Remote control re-armed in place (slice 3).</summary>
    RcReArmed = 8,

    /// <summary>Session restarted while idle to restore remote control (slice 3).</summary>
    RcRestart = 9,

    /// <summary>A liveness probe (round-trip healthcheck) failed (slice 3).</summary>
    LivenessProbeFailed = 10,

    /// <summary>
    /// A delivered message could not be verified in the Claude composer (or the submit produced
    /// no output) — the terminal is treated as wedged. Replaces the removed TUI echo probe.
    /// </summary>
    DeliveryVerificationFailed = 11,

    /// <summary>
    /// The session's context was compacted (compact-boundary transcript record). Info-level,
    /// recorded WITHOUT an alert — compaction is normal operation; the timeline row exists so
    /// operators can correlate behaviour changes with compactions.
    /// </summary>
    ContextCompacted = 12,

    /// <summary>
    /// A message delivery THREW before the terminal accepted it (runner 500/unreachable/timeout)
    /// — distinct from <see cref="DeliveryVerificationFailed"/>, where the write landed but the
    /// composer showed no evidence. The message is reverted to Pending for redelivery. Live miss
    /// 2026-08-09: briefs marked Sent before a failed write stranded four delegated tasks
    /// silently (CARD-0003/CARD-0018).
    /// </summary>
    DeliveryTransportFailed = 13,

    /// <summary>
    /// A message larger than <c>DelegationSettings.PtyInlineSafeChars</c> was typed into a terminal.
    /// Measured on 2026-08-10: above roughly 4 300 characters a single write to the ConPTY input
    /// pipe silently loses whole 1024-byte chunks of the body, so the result reads as a complete
    /// message and passes a head-or-tail liveness check. Delivery still proceeds (refusing would
    /// strand the message), but it is never again invisible: this incident is the record that the
    /// recipient may have read a splice.
    ///
    /// Note this fires on SIZE alone and is therefore not a complete guard: on 2026-08-11 four
    /// bodies well under the ceiling lost everything before their final 1024-byte chunk, raising
    /// no incident at all. See <see cref="DelegateReportUncorrelated"/>, which catches the
    /// consequence rather than the size.
    /// </summary>
    OversizedTerminalDelivery = 14,

    /// <summary>
    /// The runner could not safely bind a transcript to this agent's session and is running
    /// WITHOUT one — nothing is ingested, working/idle reads permanently idle, and a channel-bound
    /// agent cannot dispatch replies at all. Refusing is the safe outcome (CARD-0006: the
    /// alternative bound an agent to the human operator's own Claude conversation on nothing but
    /// "same cwd, written recently"), but it must never be silent. Critical when the agent has a
    /// channel binding, Warning otherwise.
    /// </summary>
    TranscriptBindFailed = 15,

    /// <summary>
    /// A transcript was bound by HEURISTIC rather than by the exact <c>&lt;session-id&gt;.jsonl</c>
    /// filename (cwd discovery, a mid-session fork, or the restart migration shim). Info-level,
    /// recorded WITHOUT an alert, mirroring <see cref="ContextCompacted"/>: the bind passed every
    /// adoption rule, but which file an agent reads from should be on the record.
    /// </summary>
    TranscriptBoundByDiscovery = 16,

    /// <summary>
    /// A delegate ended a turn with a report, but the prompt that turn answered did not carry the
    /// task's correlation marker — so the task could not be settled from it. The delegate does the
    /// work, reports, and the task sits Dispatched forever with no surface saying why.
    ///
    /// Live miss 2026-08-11 (CARD-0003): three tasks stranded overnight because the pty dropped
    /// everything before the final 1024-byte chunk of the brief, taking the head-only marker with
    /// it. The condition was already detected in code and logged at DEBUG, under a file sink set to
    /// Information — so the one event that explained three dead tasks was written nowhere.
    /// </summary>
    DelegateReportUncorrelated = 17,

    /// <summary>
    /// A delegated task settled WITHOUT the text of the response that ended its turn — so the stored
    /// report is whatever else the turn produced (mid-turn narration, most likely) and not the
    /// delegate's verdict. Raised when CARD-0046's grace expires: either settled Succeeded on that
    /// fallback text, or failed outright because the turn produced no text at all.
    ///
    /// Live miss 2026-08-13/14 (CARD-0046): six delegates lost 4 573-6 296-character reports to a
    /// bare TurnEnd emitted by the thinking record of the same API response, and every surface said
    /// the task had succeeded. The identity gate now waits for that response's own text; this is
    /// what fires when waiting does not produce it, so the SECOND occurrence is visible instead of
    /// the sixth. Warning: the task is settled either way, and a human deciding whether to re-run it
    /// needs to know the report may be preamble.
    /// </summary>
    DelegateFinalMessageMissing = 18,
}

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
    /// pipe silently loses whole 1024-byte chunks out of the MIDDLE of the body — head and tail
    /// always survive, so the result reads as a complete message and passes a head-or-tail
    /// liveness check. Delivery still proceeds (refusing would strand the message), but it is never
    /// again invisible: this incident is the record that the recipient may have read a splice.
    /// </summary>
    OversizedTerminalDelivery = 14,
}

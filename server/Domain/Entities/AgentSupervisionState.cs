using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Per-agent supervision bookkeeping (1:1 with <see cref="Agent"/>). Kept out of the Agents row so
/// supervisor churn never contends with agent updates. The ladder never gives up: failures only
/// stretch <see cref="NextRestartAt"/> further out (30-day cap), and sustained healthy uptime
/// resets the ordinary ladder. A separate durable Herdr hold requires explicit operator retry.
/// </summary>
public class AgentSupervisionState
{
    public Guid AgentId { get; set; }

    /// <summary>User-intent latch: a human stopped this agent; supervision must not restart it.</summary>
    public bool Suspended { get; set; }

    public int ConsecutiveFailures { get; set; }
    public int RestartBackoffFailures { get; set; }
    public Guid? LastObservedRestartSessionId { get; set; }
    public DateTime? LastObservedRestartStartedAt { get; set; }
    public DateTime? ContinuityHeldAt { get; set; }
    public Guid? ContinuitySessionId { get; set; }
    public StandingContinuityReason? ContinuityReason { get; set; }
    public string? ContinuityEvidence { get; set; }
    public int HerdrConsecutiveFailures { get; set; }
    public DateTime? HerdrFailureHeldAt { get; set; }
    public HerdrSupervisionFailureKind? LastHerdrFailureKind { get; set; }
    public Guid? LastHerdrObservedSessionId { get; set; }
    public DateTime? LastHerdrObservedStartedAt { get; set; }
    public DateTime? HerdrHealthySince { get; set; }
    /// <summary>
    /// CARD-0511. When this agent stopped being restarted because the connected session runner is
    /// an older build than the launch needs. Distinct from the continuity hold: it needs no
    /// operator decision, only a different runner. The supervisor releases it the moment
    /// <c>GET /capabilities</c> reports an identity other than
    /// <see cref="RunnerBuildHeldIdentity"/>, and a manual Start clears it outright. Nothing is
    /// charged to the backoff ladder while held, and no RestartScheduled/Crash rows are written:
    /// the ladder paces crash loops, and pacing a stale binary no retry can fix is pure loss.
    /// </summary>
    public DateTime? RunnerBuildHeldAt { get; set; }

    /// <summary>The runner identity the refusal was decided against; <c>"unknown"</c> releases on
    /// any answered probe.</summary>
    public string? RunnerBuildHeldIdentity { get; set; }

    /// <summary>The runner's own refusal sentence, as shown to the operator.</summary>
    public string? RunnerBuildHoldEvidence { get; set; }

    public DateTime? NextRestartAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Highest backoff tier already alerted on (0 none, 1 hourly, 2 daily) — escalation alerts fire once per tier.</summary>
    public int LastEscalationTier { get; set; }

    public DateTime? LastHealthyAt { get; set; }

    /// <summary>
    /// CARD-0312 S4. When the boot-reply watch STOPPED restarting this agent. The mechanism gets
    /// at most two consecutive probe-driven restarts; the third consecutive failure latches it off
    /// and raises the incident at <c>AlertSeverity.Error</c> instead of restarting again. Null is
    /// unlatched. Cleared by a human <c>StartAsync</c> (which already lifts the supervision latch)
    /// or by any successful reply. This is the 2026-07 lesson held in a column: the periodic
    /// liveness probe was deleted twice for false-positive-killing healthy sessions, and an
    /// unbounded restart ladder driven by a liveness verdict is that failure by another route.
    /// </summary>
    public DateTime? LivenessLatchedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>CARD-0412: typed capacity wait for this standing owner, distinct from crash NextRestartAt.</summary>
    public Guid? CapacityWaitId { get; set; }
    public string? CapacityRecoveryActionKey { get; set; }
    public DateTime? CapacityNextDueAt { get; set; }

    public Agent? Agent { get; set; }
}

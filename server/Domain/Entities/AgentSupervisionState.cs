namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Per-agent supervision bookkeeping (1:1 with <see cref="Agent"/>). Kept out of the Agents row so
/// supervisor churn never contends with agent updates. The ladder never gives up: failures only
/// stretch <see cref="NextRestartAt"/> further out (30-day cap), and sustained healthy uptime
/// resets everything.
/// </summary>
public class AgentSupervisionState
{
    public Guid AgentId { get; set; }

    /// <summary>User-intent latch: a human stopped this agent; supervision must not restart it.</summary>
    public bool Suspended { get; set; }

    public int ConsecutiveFailures { get; set; }
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

    public Agent? Agent { get; set; }
}

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
    public DateTime UpdatedAt { get; set; }

    public Agent? Agent { get; set; }
}

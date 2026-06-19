using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A message the user has lined up to send to an agent session. "Send now" messages are delivered
/// immediately and never stored; only "wait until idle" messages live here as <see cref="QueuedMessageStatus.Pending"/>
/// rows until the agent reaches a turn-end (then the oldest pending message is delivered, one per turn).
/// Persisted so a queued message survives a server restart and reattaches to the live session.
/// </summary>
public class SessionQueuedMessage
{
    public Guid Id { get; set; }
    public Guid AgentSessionId { get; set; }

    /// <summary>The text delivered into the agent's terminal (a carriage return is appended on send).</summary>
    public string Body { get; set; } = string.Empty;

    public QueuedMessageStatus Status { get; set; } = QueuedMessageStatus.Pending;

    /// <summary>FIFO ordering key — monotonic per session in enqueue order.</summary>
    public long Sequence { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CanceledAt { get; set; }

    public AgentSession AgentSession { get; set; } = null!;
}

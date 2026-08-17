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

    /// <summary>Who enqueued it (drives batching: only Channel messages coalesce).</summary>
    public QueuedMessageOrigin Origin { get; set; } = QueuedMessageOrigin.Ui;

    /// <summary>
    /// For Channel-origin messages: <c>"{provider}:{conversationId}"</c> — the batching key
    /// (only a contiguous run of the SAME conversation coalesces into one delivery).
    /// </summary>
    public string? ConversationKey { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// How many times this message has been typed into a terminal (CARD-0055). Survives the revert
    /// a failed verification does — that is the point: it is what stops an automatic retry looping
    /// forever, and at <c>MaxDeliveryAttempts</c> the message parks for a human instead.
    /// </summary>
    public int DeliveryAttempts { get; set; }

    /// <summary>When the most recent attempt started typing.</summary>
    public DateTime? LastDeliveryStartedAt { get; set; }

    /// <summary>
    /// The transcript <c>Sequence</c> floor captured just before the most recent attempt typed —
    /// null when the session had no transcript to observe. The anti-duplicate keystone: before any
    /// path re-types a previously attempted message it re-runs the prompt matcher over
    /// <c>UserPrompt</c> rows past this sequence, and a match marks the message Sent with zero
    /// writes to the terminal. Automatic retry is safe only because the retry looks before it types.
    /// </summary>
    public long? LastDeliveryBaselineSequence { get; set; }

    /// <summary>
    /// For <see cref="QueuedMessageOrigin.Channel"/> rows: when the reply this message OWES its
    /// human was settled — either a <c>ChannelReply</c> was produced for it, or it was abandoned
    /// unanswered (which raises a Critical <see cref="Domain.Enums.AgentIncidentKind.ChannelReplyLost"/>
    /// incident). Null on a Sent channel row means "somebody in a chat is still waiting". Always
    /// null on non-Channel rows — they owe nobody a reply.
    ///
    /// <para>CARD-0067. This column is the durable half of the round trip. The message coming IN
    /// was always in Postgres; the route back OUT lived in a <c>ConcurrentDictionary</c> inside
    /// <c>ChannelReplyDispatcher</c>, so a server restart between "routed" and "turn ended" voided
    /// the reply with no log line, no incident and no user-visible signal. On 2026-08-17 a restart
    /// at 09:05:01Z killed four live correlations and a family's guest list was emitted twice and
    /// published never. The reply target is now resolved from this row at dispatch time
    /// (<see cref="ConversationKey"/> carries <c>{provider}:{conversationId}</c>), and this
    /// timestamp is what keeps a restart from ANSWERING THE SAME TURN TWICE into a live chat.</para>
    /// </summary>
    public DateTime? ChannelReplySettledAt { get; set; }

    public AgentSession AgentSession { get; set; } = null!;
}

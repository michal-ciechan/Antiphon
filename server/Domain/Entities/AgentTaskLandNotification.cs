using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Immutable event payload and durable delivery obligation; receipt requires UserPrompt evidence.</summary>
public sealed class AgentTaskLandNotification
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid TaskId { get; set; }
    public Guid? LandingOperationId { get; set; }
    public Guid SourceEventId { get; set; }
    public LandNotificationKind Kind { get; set; }
    public AgentTaskReplyTo ReplyTo { get; set; }
    public Guid? ParentSessionId { get; set; }
    public string Body { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int EnqueueAttempts { get; set; }
    public LandNotificationState State { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public Guid? QueueMessageId { get; set; }
    public DateTime? EnqueuedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public long? ConfirmingPromptSequence { get; set; }
    public DateTime? WarningAt { get; set; }
    public DateTime? ErrorAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

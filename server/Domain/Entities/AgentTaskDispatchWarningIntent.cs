using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0508 S2b. Producer checkpoint for a dispatch-base warning. Preallocates the Warning event
/// and notification IDs; materialization copies these frozen fields and never reallocates.
/// </summary>
public sealed class AgentTaskDispatchWarningIntent
{
    /// <summary>Preallocated future Warning event ID. Never regenerated.</summary>
    public Guid Id { get; set; }

    /// <summary>The final agent-dispatch event of the successful claim that owns this warning.</summary>
    public Guid DispatchEventId { get; set; }

    public Guid TaskId { get; set; }

    /// <summary>Diagnostic snapshot of Attempt at capture. Not an identity key.</summary>
    public int Attempt { get; set; }

    /// <summary><c>sibling:&lt;id:N&gt;</c>, <c>base-observation-stale</c>, or <c>default-unresolved</c>.</summary>
    public string WarningKey { get; set; } = "";

    /// <summary>Preallocated note ID. No FK until the note exists.</summary>
    public Guid NotificationId { get; set; }

    public AgentTaskReplyTo ReplyTo { get; set; }

    /// <summary>Original parent snapshot; loose GUID, no cascading session FK.</summary>
    public Guid? ParentSessionId { get; set; }

    public string Detail { get; set; } = "";

    public string Body { get; set; } = "";

    public string ContentDigest { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public LandNotificationState InitialState { get; set; }

    public DateTime? MaterializedAt { get; set; }

    public int MaterializationAttempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

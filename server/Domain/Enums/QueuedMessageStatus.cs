namespace Antiphon.Server.Domain.Enums;

/// <summary>Lifecycle of a message queued to be delivered to an agent session.</summary>
public enum QueuedMessageStatus
{
    /// <summary>Waiting to be delivered (on next idle, or via an explicit send-now).</summary>
    Pending = 0,

    /// <summary>Delivered into the session's terminal.</summary>
    Sent = 1,

    /// <summary>Removed before delivery (user canceled, or the session ended).</summary>
    Canceled = 2,
}

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// An inline comment thread on a file in an agent's workspace — the review-loop unit. Anchored to
/// a line PLUS the quoted snippet at creation time: line numbers drift as the file changes, so the
/// snippet is what re-anchoring (and "outdated" detection) keys on. Threads are independent rows —
/// many can be open in parallel, each with its own status, and dispatching one to the agent rides
/// the session queue with a reply correlation, exactly like the channel bridge.
/// </summary>
public class ReviewThread
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    /// <summary>Workspace-relative path, forward slashes.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>1-based line the thread was anchored to when created.</summary>
    public int Line { get; set; }

    /// <summary>The line's text at creation — the durable anchor and the context quoted to the agent.</summary>
    public string? Snippet { get; set; }

    public ReviewThreadStatus Status { get; set; } = ReviewThreadStatus.Open;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
    public List<ReviewComment> Comments { get; set; } = [];
}

public enum ReviewThreadStatus
{
    /// <summary>Created; not yet sent to the agent.</summary>
    Open = 0,

    /// <summary>Dispatched into the agent's session; its reply will land here via correlation.</summary>
    AwaitingAgent = 1,

    /// <summary>The agent answered; the human's move (reply or resolve).</summary>
    AwaitingHuman = 2,

    /// <summary>Closed out.</summary>
    Resolved = 3,
}

/// <summary>One message in a <see cref="ReviewThread"/>.</summary>
public class ReviewComment
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public ReviewCommentAuthor Author { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ReviewThread Thread { get; set; } = null!;
}

public enum ReviewCommentAuthor
{
    Human = 0,
    Agent = 1,
    System = 2,
}

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A "work completed up to here" marker for an agent's workspace — recorded when the agent's
/// work is signed off (card moved to a terminal column, or an explicit baseline set from the
/// Files review surface). Stores the workspace HEAD commit at that moment plus the timestamp,
/// so the files view can show "changes since the last completed work": diff against the commit
/// when it is still in history, or fall back to the nearest commit before the timestamp when
/// history was rewritten (or no commit could be captured at all).
/// </summary>
public class AgentReviewCheckpoint
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    /// <summary>Workspace HEAD at capture time; null when the workspace had no git repo/commits.</summary>
    public string? CommitSha { get; set; }

    /// <summary>What marked the work complete, e.g. "Card CARD-0012 completed" or "Manual baseline".</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Agent Agent { get; set; } = null!;
}

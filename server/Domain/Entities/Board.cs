using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Board
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TrackerKind TrackerKind { get; set; } = TrackerKind.Internal;

    /// <summary>
    /// First Internal→external flip from a workflow tracker block. Never moved on reactivation
    /// after a temporary removal — it is the export watermark for gated OUT creates (CARD-0166).
    /// </summary>
    public DateTime? TrackerActivatedAt { get; set; }

    /// <summary>
    /// Repo-wide comments-pull cursor (<c>since</c>). Advanced to the pull's start time on success.
    /// </summary>
    public DateTime? TrackerCommentsPulledAt { get; set; }

    public int MaxConcurrentSessions { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();
    public ICollection<Card> Cards { get; set; } = new List<Card>();
    public ICollection<BoardWorkflowDefinition> WorkflowDefinitions { get; set; } = new List<BoardWorkflowDefinition>();
}

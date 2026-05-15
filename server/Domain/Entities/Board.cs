using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Board
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TrackerKind TrackerKind { get; set; } = TrackerKind.Internal;
    public int MaxConcurrentSessions { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();
    public ICollection<Card> Cards { get; set; } = new List<Card>();
    public ICollection<BoardWorkflowDefinition> WorkflowDefinitions { get; set; } = new List<BoardWorkflowDefinition>();
}

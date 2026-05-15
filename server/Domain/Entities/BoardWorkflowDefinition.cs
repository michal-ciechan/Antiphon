namespace Antiphon.Server.Domain.Entities;

public class BoardWorkflowDefinition
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Board Board { get; set; } = null!;
}

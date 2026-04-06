namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A named group for organizing related workflow templates (e.g., BMAD, Scrum, Kanban).
/// </summary>
public class TemplateGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<WorkflowTemplate> Templates { get; set; } = new List<WorkflowTemplate>();
}

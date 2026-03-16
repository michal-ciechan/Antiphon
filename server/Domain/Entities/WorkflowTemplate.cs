namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A reusable workflow template defining stages, executor types, and gate configuration in YAML.
/// </summary>
public class WorkflowTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string YamlDefinition { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

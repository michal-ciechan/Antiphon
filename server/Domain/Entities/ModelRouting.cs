namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Maps a workflow stage name to a specific LLM model and provider.
/// Enables routing different stages to different models (FR19, FR44).
/// </summary>
public class ModelRouting
{
    public Guid Id { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public Guid ProviderId { get; set; }
    public Guid? WorkflowTemplateId { get; set; }
    public DateTime CreatedAt { get; set; }

    public LlmProvider Provider { get; set; } = null!;
    public WorkflowTemplate? WorkflowTemplate { get; set; }
}

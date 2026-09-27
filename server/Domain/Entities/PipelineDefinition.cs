using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class PipelineDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PipelineDefinitionSource Source { get; set; }
    public Guid? ActiveRevisionId { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? ArchivedReason { get; set; }
    public string? ArchivedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public PipelineDefinitionRevision? ActiveRevision { get; set; }
    public ICollection<PipelineDefinitionRevision> Revisions { get; set; } = new List<PipelineDefinitionRevision>();
}

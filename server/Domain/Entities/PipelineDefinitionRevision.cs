namespace Antiphon.Server.Domain.Entities;

public class PipelineDefinitionRevision
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int RevisionNumber { get; set; }
    public string StagesJson { get; set; } = "[]";
    public string ContentHash { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }

    public PipelineDefinition Definition { get; set; } = null!;
    public ICollection<CardWorkflowRun> Runs { get; set; } = new List<CardWorkflowRun>();
}

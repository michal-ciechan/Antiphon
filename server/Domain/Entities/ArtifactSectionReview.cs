namespace Antiphon.Server.Domain.Entities;

public class ArtifactSectionReview
{
    public Guid Id { get; set; }
    public Guid StageExecutionId { get; set; }

    // Ordinal path like "1", "1.2", "1.2.3" — stable across heading text edits
    public string SectionPath { get; set; } = string.Empty;

    // SHA-256 (first 16 hex chars) of the section's direct body content
    public string ContentHash { get; set; } = string.Empty;

    public DateTime ReviewedAt { get; set; }

    public StageExecution StageExecution { get; set; } = null!;
}

namespace Antiphon.Server.Domain.Entities;

/// <summary>Immutable imported execution evidence; never rewrites the retained Review.</summary>
public sealed class ReviewEvidenceCorrection
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid ReviewTaskId { get; set; }
    public Guid PredecessorOutcomeId { get; set; }
    public Guid SubjectTaskId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? BoardId { get; set; }
    public Guid? CardId { get; set; }
    public string ReportDigest { get; set; } = "";
    public string SourceSha { get; set; } = "";
    public string SourceRef { get; set; } = "";
    public string RepositoryPath { get; set; } = "";
    public string SelectionPath { get; set; } = "";
    public string SelectionCommitSha { get; set; } = "";
    public string SelectionSection { get; set; } = "";
    public string SelectionDigest { get; set; } = "";
    public string RequiredRowsJson { get; set; } = "[]";
    public string CanonicalBundleDigest { get; set; } = "";
    public string ImportedEvidenceIdentity { get; set; } = "";
    public string ProducerIdentity { get; set; } = "";
    public string ValidationResultJson { get; set; } = "{}";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime RecordedAt { get; set; }
    public Guid? SuccessorOutcomeId { get; set; }
}

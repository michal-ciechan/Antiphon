namespace Antiphon.Server.Domain.Entities;

/// <summary>Immutable authorized obligations at one card/workflow/source revision.</summary>
public sealed class CardCompletionAcceptance
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid CardId { get; set; }
    public Guid BoardId { get; set; }
    public Guid? ProjectId { get; set; }
    public int Revision { get; set; }
    public Guid ExpectedCardToken { get; set; }
    public Guid? WorkflowRunId { get; set; }
    public Guid? PipelineDefinitionRevisionId { get; set; }
    public Guid? CurrentStageId { get; set; }
    public DateTime? WorkflowUpdatedAt { get; set; }
    public Guid ImplementationTaskId { get; set; }
    public string RepositoryPath { get; set; } = "";
    public string SourceRef { get; set; } = "";
    public string ReviewedSourceSha { get; set; } = "";
    public Guid ReviewEvidenceId { get; set; }
    public Guid? OperationId { get; set; }
    public string? LandedSha { get; set; }
    public string PolicyRevision { get; set; } = "";
    public string DecisionRevision { get; set; } = "";
    public string AcceptanceItemsJson { get; set; } = "[]";
    public string DeploymentItemsJson { get; set; } = "[]";
    public string? EmptyObligationsDecisionJson { get; set; }
    public string CompanionObligationJson { get; set; } = "{}";
    public bool ReadyForRelease { get; set; }
    public string Actor { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}

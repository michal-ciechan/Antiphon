using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Dtos;

public enum CompletionItemState { Pending, Satisfied }
public sealed record CompletionItem(string Id, CompletionItemState State, string? EvidenceDigest = null,
    string? RequiredBuild = null, string? RequiredVersion = null, string? ObservedBuild = null,
    string? ObservedVersion = null, string? Actor = null, DateTime? RecordedAt = null);
public sealed record EmptyCompletionDecision(string Category, string Reason, string? Actor = null, DateTime? RecordedAt = null);
public sealed record CompletionCompanion(bool Required, Guid? CardId, Guid ImplementationTaskId, Guid? OperationId);
public sealed record WriteCardCompletionAcceptanceRequest(int ExpectedRevision, Guid ExpectedCardToken,
    Guid? WorkflowRunId, Guid? PipelineDefinitionRevisionId, Guid? CurrentStageId, DateTime? WorkflowUpdatedAt,
    Guid ImplementationTaskId, string RepositoryPath, string SourceRef, string ReviewedSourceSha,
    Guid ReviewEvidenceId, Guid? OperationId, string? LandedSha, string PolicyRevision, string DecisionRevision,
    IReadOnlyList<CompletionItem> AcceptanceItems, IReadOnlyList<CompletionItem> DeploymentItems,
    EmptyCompletionDecision? EmptyObligationsDecision, CompletionCompanion Companion, bool ReadyForRelease);
public sealed record PublishedCardCompletionProposal(Guid CardId, Guid CardToken, int AcceptanceRevision,
    Guid AcceptanceId, Guid? WorkflowRunId, Guid? PipelineDefinitionRevisionId, Guid? CurrentStageId,
    DateTime? WorkflowUpdatedAt, string PolicyRevision, string DecisionRevision);
public sealed record CardCompletionView(CardCompletionAcceptance? Acceptance, string? Readiness,
    CardCompletionReceipt? Receipt);
public sealed record CompletePublishedCardResult(string Disposition, CardCompletionReceipt? Receipt);

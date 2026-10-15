using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Caller-owned handoff identity. Delivery and consumption are separate facts.</summary>
public sealed class PipelineHandoffAction
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid PredecessorTaskId { get; set; }
    public int Attempt { get; set; }
    public Guid SettlementEventId { get; set; }
    public AgentTaskRole NextStage { get; set; }
    public Guid? CardId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? WorkflowRunId { get; set; }
    public Guid? PipelineDefinitionRevisionId { get; set; }
    public Guid? CurrentStageId { get; set; }
    public DateTime? WorkflowUpdatedAt { get; set; }
    public Guid? NotificationId { get; set; }
    public Guid? QueueId { get; set; }
    public Guid? RecipientSessionId { get; set; }
    public string? RecipientGeneration { get; set; }
    public long? ReceiptSequence { get; set; }
    public string? WireDigest { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? EligibleAt { get; set; }
    public DateTime? ChildCreatedAt { get; set; }
    public DateTime? ChildDispatchedAt { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime? NextObservationAt { get; set; }
    public string? RequestDigest { get; set; }
    public string Actor { get; set; } = "";
    public Guid? ChildTaskId { get; set; }
    public DateTime CreatedAt { get; set; }
}

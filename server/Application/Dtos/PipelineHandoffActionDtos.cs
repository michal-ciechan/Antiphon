using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PipelineHandoffReceiptReference(
    Guid NotificationId, Guid QueueId, Guid RecipientSessionId, DateTime RecipientGeneration,
    long ReceiptSequence, string WireDigest);

/// <summary>Explicit caller observation, fenced against both workflow and observation history.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PipelineHandoffActionRequest(
    int Attempt, Guid SettlementEventId, AgentTaskRole NextStage,
    long ExpectedObservationSequence, string ReasonCode, string Detail,
    DateTime? NextObservationAt = null,
    Guid? WorkflowRunId = null, Guid? PipelineDefinitionRevisionId = null,
    Guid? CurrentStageId = null, DateTime? WorkflowUpdatedAt = null,
    PipelineHandoffReceiptReference? Receipt = null);

/// <summary>Read-only snapshots. Child clocks come from the actual task, never receipt time.</summary>
public sealed record PipelineHandoffActionDto(
    PipelineHandoffAction Action, IReadOnlyList<PipelineHandoffObservation> Observations,
    bool? RequestedWithinFiveMinutes, bool UnexplainedWaitRequiresInvestigation);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PipelineHandoffChildRequest(
    CreateAgentTaskRequest Task, bool AcceptSpend = false, string Trigger = "caller");

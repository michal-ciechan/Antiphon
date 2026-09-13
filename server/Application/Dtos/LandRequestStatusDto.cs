using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>Informational pre-outbox history. Never scheduled or inferred to be a receipt.</summary>
public sealed record LegacyLandReceiptDto(Guid EventId, DateTime At, string State, Guid? QueueMessageId = null,
    DateTime? ConfirmedAt = null, long? ConfirmingPromptSequence = null);

public sealed record LandRequestStatusDto(
    Guid Id, LandRequestState State, DateTime RequestedAt, DateTime? StartedAt,
    DateTime LastEvaluatedAt, DateTime LastProgressAt, double AgeSeconds, double NoProgressSeconds,
    int Attempt, string? HoldReasonCode, string? HoldDetail, Guid? HoldingTaskId,
    AgentTaskStatus? HoldingTaskStatus, DateTime? HeldSince, int HoldEpisode,
    Guid? LandingOperationId, Guid? TerminalEventId, string? ReconciliationError,
    IReadOnlyList<LandNotificationStatusDto> Notifications,
    int SchemaVersion = 1,
    string? ExpectedSourceSha = null,
    Guid? ReviewEvidenceId = null,
    LandApprovalKind ApprovalKind = LandApprovalKind.ExplicitCaller,
    LandSourceResolutionState SourceResolutionState = LandSourceResolutionState.None,
    string? LocalBeforeSha = null,
    string? RemoteSourceSha = null,
    string? CandidateSourceSha = null,
    string? ResolvedSourceSha = null,
    LandSourceRelationship SourceRelationship = LandSourceRelationship.Unknown,
    string? SourceRefusalReason = null,
    string? TerminalFailureCode = null,
    Guid? FailureDiagnosticId = null,
    string? FailureExceptionType = null,
    string? SourceDiagnosticCommand = null,
    int? SourceDiagnosticExitCode = null,
    string? SourceDiagnosticCode = null,
    string? SourceDiagnosticExceptionType = null)
{
    public static LandRequestStatusDto From(AgentTaskLandRequest r, DateTime now, IReadOnlyList<LandNotificationStatusDto> notifications)
        => new(r.Id, r.State, r.RequestedAt, r.StartedAt, r.LastEvaluatedAt, r.LastProgressAt,
            (now-r.RequestedAt).TotalSeconds, (now-r.LastProgressAt).TotalSeconds, r.Attempt,
            r.HoldReasonCode, r.HoldDetail, r.HoldingTaskId, r.HoldingTaskStatus, r.HeldSince, r.HoldEpisode,
            r.LandingOperationId, r.TerminalEventId, r.ReconciliationError, notifications,
            r.SchemaVersion, r.ExpectedSourceSha, r.ReviewEvidenceId, r.ApprovalKind, r.SourceResolutionState,
            r.LocalBeforeSha, r.RemoteSourceSha, r.CandidateSourceSha, r.ResolvedSourceSha,
            r.SourceRelationship, r.SourceRefusalReason,
            r.TerminalFailureCode, r.FailureDiagnosticId, r.FailureExceptionType,
            r.SourceDiagnosticCommand, r.SourceDiagnosticExitCode, r.SourceDiagnosticCode,
            r.SourceDiagnosticExceptionType);
}

public sealed record LandNotificationStatusDto(Guid Id, LandNotificationKind Kind, LandNotificationState State,
    Guid? DestinationSessionId, Guid? QueueMessageId, DateTime CreatedAt, DateTime NextAttemptAt,
    int EnqueueAttempts, string? LastErrorCode, DateTime? ConfirmedAt, long? ConfirmingPromptSequence)
{
    public static LandNotificationStatusDto From(AgentTaskLandNotification n) => new(n.Id, n.Kind, n.State,
        n.ParentSessionId, n.QueueMessageId, n.CreatedAt, n.NextAttemptAt, n.EnqueueAttempts, n.LastErrorCode,
        n.ConfirmedAt, n.ConfirmingPromptSequence);
}

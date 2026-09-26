using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record LandingEvidenceDto(Guid OperationId, LandPhase Phase, LandOperationMode Mode,
    LandPublicationOutcome Publication, LandCleanupStatus Cleanup, string SourceSha, string? VerifiedSha,
    string? RemoteSha, DateTime? RemoteConfirmedAt, string DestinationRef, string? Reason,
    string? ReviewedSha = null, string? PreparationInputSha = null, string? SourceRemoteSha = null,
    Guid? ApprovalLandRequestId = null, Guid? ReviewEvidenceId = null, int SchemaVersion = 1,
    Guid? CleanupAttemptId = null, WorktreeCleanupReference? CleanupCapture = null,
    LandRecoveryMode RecoveryMode = LandRecoveryMode.None, AgentTaskStatus? RecoveryOwnerStatus = null,
    Guid? RecoverySourceTaskId = null, string? RecoverySourceFullRef = null,
    string? RecoveryOwnerRemoteBeforeSha = null, string? RecoveryOwnerRemoteAfterSha = null,
    string? RecoveryRelationship = null)
{
    public static LandingEvidenceDto From(AgentTaskLanding op) => new(op.Id, op.Phase, op.Mode,
        new Services.AgentTaskLandingState().HasPublication(op) ? op.Publication
            : op.Publication == LandPublicationOutcome.Refused ? LandPublicationOutcome.Refused : LandPublicationOutcome.Unconfirmed,
        op.Cleanup, op.OriginalSourceSha, op.VerifiedSourceSha, op.ObservedRemoteTargetSha,
        op.RemoteConfirmedAt, op.DestinationFullRef, op.LastReason,
        op.ReviewedSourceSha, op.PreparationInputSha, op.SourceRemoteSha,
        op.ApprovalLandRequestId, op.ReviewEvidenceId, op.SchemaVersion,
        RecoveryMode: op.RecoveryMode, RecoveryOwnerStatus: op.RecoveryOwnerStatus,
        RecoverySourceTaskId: op.RecoverySourceTaskId, RecoverySourceFullRef: op.RecoverySourceFullRef,
        RecoveryOwnerRemoteBeforeSha: op.RecoveryOwnerRemoteBeforeSha,
        RecoveryOwnerRemoteAfterSha: op.RecoveryOwnerRemoteAfterSha,
        RecoveryRelationship: op.RecoveryRelationship);
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Checkpoint policy; callers must acknowledge persistence before dependent I/O.</summary>
public sealed class AgentTaskLandingState
{
    public bool HasPublication(AgentTaskLanding operation) => HasIdentity(operation)
        && operation.RemoteConfirmedAt is not null
        && operation.Publication is LandPublicationOutcome.Landed or LandPublicationOutcome.AlreadyPresent
        && IsOid(operation.VerifiedSourceSha) && IsOid(operation.ObservedRemoteTargetSha)
        && operation.DestinationFullRef == operation.TargetFullRef
        && operation.RemoteFingerprint.Length == 64
        && HasVerification(operation)
        && operation.RecoveryRefPrefix == $"refs/antiphon/land/{operation.TaskId:N}/{operation.Id:N}"
        && operation.ConfirmationMethod == "push-endpoint-read-fetch-ancestry";

    public void Transition(AgentTaskLanding operation, LandPhase next, DateTime now)
    {
        if (operation.SchemaVersion is not 1 and not 2) throw new InvalidOperationException("landing_schema_unsupported");
        var permitted = (operation.Phase, next) switch
        {
            (LandPhase.Inspected, LandPhase.RecoveryPinned) => operation.SourcePinned && operation.TargetPinned,
            (LandPhase.RecoveryPinned, LandPhase.RebaseStarted) => true,
            (LandPhase.RebaseStarted, LandPhase.Prepared) => operation.PreparedPinned && IsOid(operation.RebasedSourceSha),
            (LandPhase.RecoveryPinned, LandPhase.Verified) => operation.VerificationSkipReason == "exact_remote_containment",
            (LandPhase.Prepared, LandPhase.Verified) => true,
            (LandPhase.Verified, LandPhase.TargetAdvanceStarted) => true,
            (LandPhase.TargetAdvanceStarted, LandPhase.LocalTargetAdvanced) => operation.LocalTargetAfterSha == operation.VerifiedSourceSha,
            (LandPhase.LocalTargetAdvanced, LandPhase.PushStarted) => operation.PushStartedAt is not null,
            (LandPhase.Verified or LandPhase.PushStarted or LandPhase.LocalTargetAdvanced, LandPhase.PublicationConfirmed) => HasPublication(operation),
            (LandPhase.PublicationConfirmed, LandPhase.CleanupStarted) => HasPublication(operation)
                && operation.CleanupStartedAt is not null && operation.ExpectedDeletionSha == operation.VerifiedSourceSha,
            (LandPhase.CleanupStarted, LandPhase.Complete) => HasPublication(operation)
                && operation.DirectoryRemoved && operation.RegistrationRemoved && operation.BranchRemoved,
            (_, LandPhase.Refused) => !HasPublication(operation) && operation.Phase != LandPhase.Complete,
            _ => false,
        };
        if (next is LandPhase.Verified or LandPhase.TargetAdvanceStarted or LandPhase.PushStarted)
            permitted &= HasVerification(operation);
        if (next != LandPhase.Refused) permitted &= HasIdentity(operation);
        if (!permitted) throw new InvalidOperationException($"landing_transition_refused:{operation.Phase}:{next}");
        operation.Phase = next;
        operation.UpdatedAt = now;
        operation.ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>An explicit retry after terminal refusal requires fresh accepted inspection, even for unchanged source.</summary>
    public bool CanReplaceRefused(AgentTaskLanding previous, LandSourceInspection freshInspection,
        bool explicitRequest, bool leaseHeld) => CanReplaceRefused(previous, freshInspection, explicitRequest, leaseHeld, null);

    public bool CanReplaceRefused(AgentTaskLanding previous, LandSourceInspection freshInspection,
        bool explicitRequest, bool leaseHeld, string? expectedSourceSha)
    {
        if (!explicitRequest || !leaseHeld || previous.Phase != LandPhase.Refused || HasPublication(previous)
            || !freshInspection.Accepted
            || freshInspection.Snapshot!.Coordinates.TaskId != previous.TaskId)
            return false;
        if (previous.SchemaVersion is not 1 and not 2) return false;
        if (previous.SchemaVersion == 1) return true;
        _ = expectedSourceSha;
        return previous.ApprovalLandRequestId is not null
            && previous.ReviewedSourceSha == previous.OriginalSourceSha;
    }

    public bool HasV2Approval(AgentTaskLanding operation) =>
        operation.SchemaVersion == 2 && HasV2ApprovalStatic(operation);

    public bool HasLineage(AgentTaskLanding operation) =>
        operation.SchemaVersion == 1 || (operation.SchemaVersion == 2 && LineageHolds(operation));

    private static bool HasVerification(AgentTaskLanding operation)
    {
        var expected = operation.RebasedSourceSha
            ?? operation.PreparationInputSha
            ?? operation.OriginalSourceSha;
        var derivation = operation.SchemaVersion == 2
            && operation.PreparationInputSha is { } input && input != operation.OriginalSourceSha;
        var baseUnchanged = operation.VerificationSkipReason == "base_unchanged"
            && operation.RebasedSourceSha == operation.OriginalSourceSha
            && string.IsNullOrWhiteSpace(operation.VerificationFilter)
            && !derivation;
        var containment = operation.VerificationSkipReason == "exact_remote_containment"
            && operation.RebasedSourceSha is null
            && (!derivation || operation.VerifiedSourceSha == operation.PreparationInputSha);
        return IsOid(operation.VerifiedSourceSha)
            && operation.VerifiedAt is not null && operation.SourcePinned && operation.TargetPinned
            && (operation.RebasedSourceSha is null || operation.PreparedPinned)
            && operation.VerifiedSourceSha == expected
            && (operation.VerificationPassed || baseUnchanged || containment);
    }

    private static bool IsOid(string? value) => GitObjectId.IsFull(value);

    private static bool HasIdentity(AgentTaskLanding operation) =>
        operation.SchemaVersion is 1 or 2
        && operation.Id != Guid.Empty && operation.TaskId != Guid.Empty
        && IsOid(operation.OriginalSourceSha) && IsOid(operation.TargetBeforeSha)
        && operation.SourceFullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
        && operation.TargetFullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
        && operation.SourceFullRef != operation.TargetFullRef
        && operation.DestinationFullRef == operation.TargetFullRef
        && !string.IsNullOrWhiteSpace(operation.RepositoryPath)
        && !string.IsNullOrWhiteSpace(operation.CommonDirectory)
        && !string.IsNullOrWhiteSpace(operation.WorktreePath)
        && !string.IsNullOrWhiteSpace(operation.GitDirectory)
        && operation.RemoteFingerprint.Length == 64
        && operation.RecoveryRefPrefix == $"refs/antiphon/land/{operation.TaskId:N}/{operation.Id:N}"
        && (operation.SchemaVersion == 1 || HasV2ApprovalStatic(operation));

    private static bool HasV2ApprovalStatic(AgentTaskLanding operation) =>
        operation.ApprovalLandRequestId is { } requestId && requestId != Guid.Empty
        && IsOid(operation.ReviewedSourceSha)
        && operation.ReviewedSourceSha == operation.OriginalSourceSha
        && LineageHolds(operation);

    private static bool LineageHolds(AgentTaskLanding operation)
    {
        var input = operation.PreparationInputSha;
        if (input is null || input == operation.OriginalSourceSha)
            return operation.PreviousPreparationOperationId is null;
        return operation.PreviousPreparationOperationId is { } predecessor && predecessor != Guid.Empty
            && predecessor != operation.Id && IsOid(input);
    }
}

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
        if (operation.SchemaVersion is not (1 or 2 or 3)) throw new InvalidOperationException("landing_schema_unsupported");
        var permitted = (operation.Phase, next) switch
        {
            (LandPhase.Inspected, LandPhase.RecoveryPinned) => operation.SourcePinned && operation.TargetPinned,
            (LandPhase.RecoveryPinned, LandPhase.RebaseStarted) => true,
            (LandPhase.RebaseStarted, LandPhase.Prepared) => operation.PreparedPinned && IsOid(operation.RebasedSourceSha),
            (LandPhase.RecoveryPinned, LandPhase.Verified) => operation.VerificationSkipReason == "exact_remote_containment",
            (LandPhase.Prepared, LandPhase.Verified) => true,
            (LandPhase.Verified, LandPhase.TargetAdvanceStarted) => operation.SchemaVersion != 3,
            // CARD-0688 D-4: schema 3 pushes straight from Verified; local target is advanced after publication.
            (LandPhase.Verified, LandPhase.PushStarted) => operation.SchemaVersion == 3 && operation.PushStartedAt is not null,
            (LandPhase.TargetAdvanceStarted, LandPhase.LocalTargetAdvanced) => operation.LocalTargetAfterSha == operation.VerifiedSourceSha,
            (LandPhase.LocalTargetAdvanced, LandPhase.PushStarted) => operation.SchemaVersion != 3 && operation.PushStartedAt is not null,
            (LandPhase.Verified or LandPhase.PushStarted or LandPhase.LocalTargetAdvanced, LandPhase.PublicationConfirmed) => HasPublication(operation),
            (LandPhase.PublicationConfirmed, LandPhase.CleanupStarted) => HasPublication(operation)
                && operation.CleanupStartedAt is not null && operation.ExpectedDeletionSha == ExpectedDeletion(operation),
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
        _ = expectedSourceSha;
        return freshInspection.Accepted && freshInspection.Snapshot!.Coordinates.TaskId == previous.TaskId
            && CanReplaceRefused(previous, explicitRequest, leaseHeld);
    }

    /// <summary>CARD-0688: replacing a refused operation needs no worktree inspection; the replacement reads the
    /// branch ref itself. Only an explicit request under the lease may replace, and never a publication.</summary>
    public bool CanReplaceRefused(AgentTaskLanding previous, bool explicitRequest, bool leaseHeld)
    {
        if (!explicitRequest || !leaseHeld || previous.Phase != LandPhase.Refused || HasPublication(previous))
            return false;
        if (previous.SchemaVersion is not (1 or 2 or 3)) return false;
        if (previous.SchemaVersion == 1) return true;
        return previous.ApprovalLandRequestId is not null
            && previous.ReviewedSourceSha == previous.OriginalSourceSha;
    }

    public bool HasV2Approval(AgentTaskLanding operation) =>
        operation.SchemaVersion is 2 or 3 && HasV2ApprovalStatic(operation);

    public bool HasLineage(AgentTaskLanding operation) =>
        operation.SchemaVersion == 1 || (operation.SchemaVersion is 2 or 3 && LineageHolds(operation));

    private static bool HasVerification(AgentTaskLanding operation)
    {
        var expected = operation.RebasedSourceSha
            ?? operation.PreparationInputSha
            ?? operation.OriginalSourceSha;
        var derivation = operation.SchemaVersion is 2 or 3
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
        operation.SchemaVersion is 1 or 2 or 3
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
        && (operation.SchemaVersion == 1 || HasV2ApprovalStatic(operation))
        && (operation.SchemaVersion != 3 || IsOid(operation.SourceLocalSha) && IsOid(operation.LocalTargetBeforeSha)
            && !string.IsNullOrWhiteSpace(operation.LandWorktreePath));

    /// <summary>CARD-0688 D-6: schema 3 never moves the task branch, so cleanup deletes it at the SHA it had when
    /// the operation was created; schema 1/2 rebased the branch itself and delete it at the landed SHA.</summary>
    public static string? ExpectedDeletion(AgentTaskLanding operation) =>
        operation.SchemaVersion == 3 ? operation.SourceLocalSha : operation.VerifiedSourceSha;

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

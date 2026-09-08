using Antiphon.Server.Application.Dtos;
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
        && operation.VerifiedSourceSha == (operation.RebasedSourceSha ?? operation.OriginalSourceSha)
        && operation.VerifiedAt is not null
        && (operation.VerificationPassed || operation.VerificationSkipReason is "base_unchanged" or "exact_remote_containment")
        && operation.SourcePinned && operation.TargetPinned
        && operation.RecoveryRefPrefix == $"refs/antiphon/land/{operation.TaskId:N}/{operation.Id:N}"
        && operation.ConfirmationMethod == "push-endpoint-read-fetch-ancestry";

    public void Transition(AgentTaskLanding operation, LandPhase next, DateTime now)
    {
        if (operation.SchemaVersion != 1) throw new InvalidOperationException("landing_schema_unsupported");
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
            permitted &= IsOid(operation.VerifiedSourceSha) && operation.VerifiedAt is not null
                && (operation.VerificationPassed || operation.VerificationSkipReason is "base_unchanged" or "exact_remote_containment")
                && operation.VerifiedSourceSha == (operation.RebasedSourceSha ?? operation.OriginalSourceSha);
        if (next != LandPhase.Refused) permitted &= HasIdentity(operation);
        if (!permitted) throw new InvalidOperationException($"landing_transition_refused:{operation.Phase}:{next}");
        operation.Phase = next;
        operation.UpdatedAt = now;
        operation.ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>F1: a human may resolve/abort in place without changing task coordinates.</summary>
    public bool CanReplaceRefused(AgentTaskLanding previous, LandSourceInspection freshInspection,
        bool explicitRequest, bool leaseHeld) => explicitRequest && leaseHeld && previous.SchemaVersion == 1
        && previous.Phase == LandPhase.Refused && !HasPublication(previous)
        && freshInspection.Accepted
        && freshInspection.Snapshot!.Coordinates.TaskId == previous.TaskId
        && (previous.LastReason == "interrupted_rebase_requires_inspection"
            || freshInspection.Snapshot.Coordinates.SourceFullRef != previous.SourceFullRef
            || freshInspection.Snapshot.HeadSha != previous.OriginalSourceSha
            || freshInspection.Snapshot.Coordinates.TargetFullRef != previous.TargetFullRef
            || freshInspection.Snapshot.CommonDirectory != previous.CommonDirectory
            || freshInspection.Snapshot.RegisteredPath != previous.WorktreePath);

    private static bool IsOid(string? value) => value is { Length: 40 or 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool HasIdentity(AgentTaskLanding operation) => operation.SchemaVersion == 1
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
        && operation.RecoveryRefPrefix == $"refs/antiphon/land/{operation.TaskId:N}/{operation.Id:N}";
}

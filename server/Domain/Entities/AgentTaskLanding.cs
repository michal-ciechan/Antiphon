using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Immutable coordinates and monotonic evidence for one landing attempt history.</summary>
public sealed class AgentTaskLanding
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public bool Active { get; set; } = true;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public LandPhase Phase { get; set; }
    public LandPublicationOutcome Publication { get; set; } = LandPublicationOutcome.Unconfirmed;
    public LandCleanupStatus Cleanup { get; set; }
    public LandOperationMode Mode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? LastReason { get; set; }
    public string RepositoryPath { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string GitDirectory { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string OriginalSourceSha { get; set; } = "";
    public string? RebasedSourceSha { get; set; }
    public string? VerifiedSourceSha { get; set; }
    public string? VerificationCommand { get; set; }
    public string? VerificationFilter { get; set; }
    public bool VerificationPassed { get; set; }
    public string? VerificationSkipReason { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string TargetFullRef { get; set; } = "";
    public string TargetBeforeSha { get; set; } = "";
    public bool TargetCheckoutRecorded { get; set; }
    public string? TargetCheckoutPath { get; set; }
    public DateTime? RebaseStartedAt { get; set; }
    public DateTime? PreparedAt { get; set; }
    public DateTime? VerificationStartedAt { get; set; }
    public DateTime? CleanupCompletedAt { get; set; }
    public string? LocalTargetAfterSha { get; set; }
    public string RemoteName { get; set; } = "origin";
    public string DestinationFullRef { get; set; } = "";
    public string RemoteFingerprint { get; set; } = "";
    public string? RemoteBeforeSha { get; set; }
    public string? ObservedRemoteTargetSha { get; set; }
    public DateTime? RemoteConfirmedAt { get; set; }
    public string? ConfirmationMethod { get; set; }
    public DateTime? PushStartedAt { get; set; }
    public int? PushExitCode { get; set; }
    public string RecoveryRefPrefix { get; set; } = "";
    public bool SourcePinned { get; set; }
    public bool TargetPinned { get; set; }
    public bool PreparedPinned { get; set; }
    public DateTime? CleanupStartedAt { get; set; }
    public string? ExpectedDeletionSha { get; set; }
    public bool DirectoryRemoved { get; set; }
    public bool RegistrationRemoved { get; set; }
    public bool BranchRemoved { get; set; }
    public int? ChildProcessId { get; set; }
    public long? ChildProcessStartTicks { get; set; }
    public string? ChildOperation { get; set; }

    public Guid? ApprovalLandRequestId { get; set; }
    public string? ReviewedSourceSha { get; set; }
    public Guid? ReviewEvidenceId { get; set; }
    public LandRecoveryMode RecoveryMode { get; set; }
    public AgentTaskStatus? RecoveryOwnerStatus { get; set; }
    public Guid? RecoverySourceTaskId { get; set; }
    public string? RecoverySourceFullRef { get; set; }
    public string? RecoveryStartBaseSha { get; set; }
    public string? RecoveryLocalBeforeSha { get; set; }
    public string? RecoveryOwnerRemoteBeforeSha { get; set; }
    public string? RecoveryOwnerRemoteAfterSha { get; set; }
    public string? RecoveryRelationship { get; set; }
    public bool? RecoveryPatchesContained { get; set; }
    public string? RecoveryUncontainedPatches { get; set; }
    public Guid? SupersedesRequestId { get; set; }
    public string? PreparationInputSha { get; set; }
    public Guid? PreviousPreparationOperationId { get; set; }
    public string? SourceRemoteSha { get; set; }
    public string? SourceRemoteRef { get; set; }
    public string? SourceRemoteFingerprint { get; set; }
    public DateTime? SourceRemoteObservedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public LandApprovalKind ApprovalKind { get; set; }

    // CARD-0688 schema 3: the source is a ref, the land runs in one detached land worktree, and the
    // canonical checkout is fast-forwarded after publication.
    /// <summary>The task branch tip at op creation. The land never moves the branch; cleanup deletes it here.</summary>
    public string? SourceLocalSha { get; set; }
    public string? LandWorktreePath { get; set; }
    /// <summary>Local target at op creation; an ancestor of (or equal to) the observed remote target.</summary>
    public string? LocalTargetBeforeSha { get; set; }
    public DateTime? LandWorkspaceReadyAt { get; set; }
    public DateTime? CanonicalAdvanceStartedAt { get; set; }
    public DateTime? CanonicalAdvancedAt { get; set; }
    /// <summary>Why the canonical checkout was not advanced after publication; a residue, never a refusal.</summary>
    public string? CanonicalAdvanceReason { get; set; }
}

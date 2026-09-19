using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Dtos;

public enum WorktreeRemovalPurpose { Publication, LocalMerge, Verification, SettledTask }

/// <summary>Coordinates to recheck, never authority by themselves.</summary>
public sealed record WorktreeRemovalRequest(WorktreeRemovalPurpose Purpose,
    LandSourceCoordinates Source, string CommonDirectory, string GitDirectory,
    string ExpectedSourceSha, string ExpectedTargetSha, Guid? LandingId, RepositoryLease Lease,
    bool TargetCheckoutRecorded = false, string? TargetCheckoutPath = null, Guid? VerificationSealId = null,
    WorktreeCleanupContext? CleanupContext = null, string? ManagedRoot = null,
    Guid? RetirementId = null, bool HasDeletionIntent = false);

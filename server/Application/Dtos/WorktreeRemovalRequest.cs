using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Dtos;

public enum WorktreeRemovalPurpose { Publication, LocalMerge, Verification, SettledTask }

/// <summary>Coordinates to recheck, never authority by themselves.</summary>
public sealed record WorktreeRemovalRequest(WorktreeRemovalPurpose Purpose,
    LandSourceCoordinates Source, string CommonDirectory, string GitDirectory,
    string ExpectedSourceSha, string ExpectedTargetSha, Guid? LandingId, RepositoryLease Lease,
    bool TargetCheckoutRecorded = false, string? TargetCheckoutPath = null, Guid? VerificationSealId = null,
    WorktreeCleanupContext? CleanupContext = null, string? ManagedRoot = null,
    Guid? RetirementId = null, bool HasDeletionIntent = false,
    string? LandedSha = null, Guid? CardDoneEndpointId = null,
    // CARD-1717 D-1. Re-read immediately before each irreversible step. Null skips the gate
    // (caller-filed, publication, local merge); a system:desktop-legacy-policy release without it is
    // refused (B-B-1). A reason preserves the bytes.
    Func<CancellationToken, ValueTask<string?>>? LiveAuthorization = null,
    // CARD-1708. Set for a system:desktop-legacy-policy release: before anything moves, each commit only the tree's
    // HEAD reflog or its branch reflog reaches gets refs/antiphon/salvage/<SalvageScope>/<sha>; a failure refuses.
    string? SalvageScope = null);

using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Dtos;

public enum WorktreeRemovalPurpose { Publication, LocalMerge }

/// <summary>Coordinates to recheck, never authority by themselves.</summary>
public sealed record WorktreeRemovalRequest(WorktreeRemovalPurpose Purpose,
    LandSourceCoordinates Source, string CommonDirectory, string GitDirectory,
    string ExpectedSourceSha, string ExpectedTargetSha, Guid? LandingId, RepositoryLease Lease);

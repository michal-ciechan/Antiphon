using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

public sealed record VerificationCleanupSeal(Guid Id, long Revision, VerificationCreationCoordinates Creation,
    Guid[] Executions, bool NeverReserved, DateTime SealedAt);

/// <summary>Worker restoration evidence is separate from runtime-owned process custody.</summary>
public sealed record VerificationRestoration(int SchemaVersion, VerificationSourceIdentity Source,
    Guid CreationId, bool Restored, string Disposition, string ReportSha256, VerificationOutput[] Outputs);
public sealed record VerificationOutput(string RelativePath, string Sha256);
public sealed record VerificationRemovalAuthority(VerificationCleanupSeal Seal, VerificationRestoration Restoration,
    DateTime? RemovalStartedAt = null);

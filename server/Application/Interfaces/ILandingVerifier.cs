using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface ILandingVerifier
{
    Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct);
    Task<LandingVerification> VerifyAsync(string worktree, string? filter, LandingVerificationCorrelation correlation, CancellationToken ct)
        => VerifyAsync(worktree, filter, ct);
}

public sealed record LandingVerificationCorrelation(Guid TaskId, Guid OperationId, Guid? RequestId,
    string? ArtifactsPath = null);

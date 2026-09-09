using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface ILandingVerifier
{
    Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct);
}

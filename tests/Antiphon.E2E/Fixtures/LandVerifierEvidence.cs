using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.E2E.Fixtures;

/// <summary>Red stub: forwards the land verifier and records nothing.</summary>
internal sealed class LandVerifierEvidence(string root, ILandingVerifier inner, TimeSpan? heartbeat = null, TimeSpan? budget = null)
    : ILandingVerifier
{
    public Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        => inner.VerifyAsync(worktree, filter, ct);

    public Task<LandingVerification> VerifyAsync(string worktree, string? filter,
        LandingVerificationCorrelation correlation, CancellationToken ct)
        => inner.VerifyAsync(worktree, filter, correlation, ct);
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class LandingVerifier : ILandingVerifier
{
    public async Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
    {
        var result = await AgentTaskLandService.VerifyAsync(worktree, filter, ct);
        return new(result.Ok, result.Ok ? result.Description : result.Step + " failed");
    }
}

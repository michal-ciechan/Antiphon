using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class LandingVerifier : ILandingVerifier
{
    public async Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
    {
        var result = await AgentTaskLandService.VerifyWithObserverAsync(worktree, filter, new Observer(worktree), ct);
        return new(result.Ok, result.Ok ? result.Description : result.Step + " failed");
    }

    private sealed class Observer(string repository) : ILandingChildObserver
    {
        private RepositoryChildJournal? _journal;
        public async Task BeforeStartAsync(CancellationToken ct) => _journal = await RepositoryChildJournal.BeginAsync(repository, ct);
        public Task StartedAsync(int processId, long startTicks, CancellationToken ct) => _journal!.StartedAsync(processId, startTicks, ct);
        public Task ExitedAsync(CancellationToken ct) => _journal!.ExitedAsync(ct);
    }
}

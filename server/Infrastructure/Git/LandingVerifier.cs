using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class LandingVerifier(ILogger<LandingVerifier>? logger = null) : ILandingVerifier
{
    public async Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
    {
        var result = await AgentTaskLandService.VerifyWithObserverAsync(worktree, filter, new Observer(worktree, logger, null), ct);
        return new(result.Ok, result.Ok ? result.Description : result.Step + " failed");
    }

    public async Task<LandingVerification> VerifyAsync(string worktree, string? filter,
        LandingVerificationCorrelation correlation, CancellationToken ct)
    {
        var result = await AgentTaskLandService.VerifyWithObserverAsync(worktree, filter, new Observer(worktree, logger, correlation), ct);
        return new(result.Ok, result.Ok ? result.Description : result.Step + " failed");
    }

    private sealed class Observer(string repository, ILogger<LandingVerifier>? logger, LandingVerificationCorrelation? correlation) : ILandingChildObserver
    {
        private RepositoryChildJournal? _journal;
        private int? _processId;
        private long? _startTicks;
        public async Task BeforeStartAsync(CancellationToken ct) => _journal = await RepositoryChildJournal.BeginAsync(repository, ct);
        public async Task StartedAsync(int processId, long startTicks, CancellationToken ct)
        {
            await _journal!.StartedAsync(processId, startTicks, ct);
            _processId = processId; _startTicks = startTicks;
        }
        public Task ExitedAsync(CancellationToken ct) => _journal!.ExitedAsync(ct);
        public void Completed()
        {
            try { logger?.LogInformation("Landing verifier joined task {TaskId} operation {OperationId} request {RequestId} child {ProcessId} start {StartTicks}; exit and streams drained",
                correlation?.TaskId, correlation?.OperationId, correlation?.RequestId, _processId, _startTicks); }
            catch (Exception) { }
        }
    }
}

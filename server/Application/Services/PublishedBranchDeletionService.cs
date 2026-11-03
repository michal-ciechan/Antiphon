using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed record PublishedBranchDeletionRow(Guid OperationId, Guid TaskId, string SourceFullRef, string Outcome, string Reason);

public sealed record PublishedBranchDeletionRunResult(bool Enabled, bool DryRun, int Examined, int PushAttempts,
    string? Stopped, IReadOnlyList<PublishedBranchDeletionRow> Rows);

/// <summary>CARD-0824 S2. Deletes a confirmed-published land's origin branch after the delay. Stub: does nothing.</summary>
public sealed class PublishedBranchDeletionService(AppDbContext db, ILandingGit git, IRepositoryMutationLease leases,
    IRunnerBranchRefs runners, IOptionsMonitor<LandingSettings> settings, TimeProvider clock,
    ILogger<PublishedBranchDeletionService> logger)
{
    public Task<PublishedBranchDeletionRunResult> RunAsync(CancellationToken ct)
    {
        _ = (db, git, leases, runners, settings, clock, logger);
        return Task.FromResult(new PublishedBranchDeletionRunResult(false, false, 0, 0, null, []));
    }
}

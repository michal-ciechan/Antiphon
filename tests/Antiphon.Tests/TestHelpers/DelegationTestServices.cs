using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The one legal way to put the delegation worktree graph into a hand-built test
/// <see cref="ServiceCollection"/> (CARD-0297).
///
/// <para>Every dispatcher suite copies a ~40-line <c>ServiceCollection</c>, and each copy meant to
/// register the same trio (<see cref="IWorktreeManager"/>, <see cref="IGitService"/>,
/// <see cref="DelegationWorktreeService"/>). When <see cref="DelegationWorktreeService"/> gained a
/// <see cref="GitWorkspaceService"/> constructor dependency (ae596005 / c4d7e0d, the <c>-Land</c>
/// deliverable work) eight of those copies silently went red at
/// <c>GetRequiredService&lt;AgentTaskDispatcher&gt;()</c> and the rest each grew their own
/// one-liner with a CARD-0230 comment. This helper is the funnel so the next clone cannot miss it.</para>
///
/// <para>Real production types, no fakes: <see cref="GitWorkspaceService"/>'s constructor only
/// requires logging (its <c>GitProcessGate</c> and <c>IOptions&lt;GitSettings&gt;</c> are optional),
/// and nothing here reaches a git subprocess unless a test takes a Worktree / deliverable arm.
/// <c>GitProcessGate</c> is deliberately NOT registered; the service falls back to its shared gate,
/// and a suite that needs the concurrency cap registers the gate itself.</para>
///
/// <para>Everything is <c>TryAdd</c>, so a harness that already registered a fake
/// <see cref="IWorktreeManager"/> (as <see cref="BridgeQueueHarness"/> does) keeps its fake, and a
/// harness that still carries the old <c>AddSingleton&lt;GitWorkspaceService&gt;()</c> one-liner
/// is not a duplicate. The two things the helper assumes are already present, because every
/// dispatcher harness already has them, are <c>AddLogging()</c> and a <see cref="TimeProvider"/>
/// (<c>WorktreeManager</c>'s clock).</para>
/// </summary>
internal static class DelegationTestServices
{
    /// <summary>
    /// <see cref="GitWorkspaceService"/> alone, for harnesses that need it for
    /// <c>AgentTaskReplyService</c> / <c>DelegateBindRefusalRecovery</c> /
    /// <c>AgentReviewCheckpointService</c> but have no worktree graph of their own.
    /// </summary>
    public static IServiceCollection AddGitWorkspaceService(this IServiceCollection services)
    {
        services.AddOptions<global::Antiphon.SessionRunner.Contracts.GrokRulesSettings>();
        services.TryAddSingleton<GrokRulesRefreshService>();
        services.TryAddSingleton<GitWorkspaceService>();
        services.AddOptions<DelegationSettings>();
        services.TryAddSingleton<IAgentReportStore, Antiphon.Server.Infrastructure.Files.AgentReportStore>();
        return services;
    }

    /// <summary>
    /// The production worktree graph: <c>IOptions&lt;GitSettings&gt;</c>, the real
    /// <see cref="WorktreeManager"/> and <see cref="GitService"/>, <see cref="GitWorkspaceService"/>,
    /// and the scoped <see cref="DelegationWorktreeService"/> that needs all four.
    /// </summary>
    /// <param name="gitSettings">
    /// The suite's <see cref="GitSettings"/> (typically a per-suite <c>WorktreeBasePath</c>). This is
    /// the only <c>GitSettings</c> registration a harness should make; drop any
    /// <c>AddSingleton(Options.Create(new GitSettings …))</c> line in favour of passing it here.
    /// </param>
    public static IServiceCollection AddDelegationWorktreeGraph(
        this IServiceCollection services,
        GitSettings? gitSettings = null)
    {
        services.TryAddSingleton(Options.Create(gitSettings ?? new GitSettings()));
        services.TryAddSingleton<IWorktreeManager, WorktreeManager>();
        services.TryAddSingleton<IGitService, GitService>();
        services.TryAddSingleton<ILandingGit, LandingGit>();
        services.TryAddSingleton<IWorktreeRemovalEvidence, Antiphon.Server.Infrastructure.Data.WorktreeRemovalEvidence>();
        services.TryAddSingleton<GuardedWorktreeRemoval>();
        services.TryAddSingleton<IRepositoryMutationLease, RepositoryMutationLease>();
        services.TryAddScoped<AgentTaskLandingState>();
        services.TryAddSingleton<ILandingVerifier, LandingVerifier>();
        services.TryAddScoped<AgentTaskLandingProtocol>();
        services.AddGitWorkspaceService();
        services.TryAddScoped<DelegationWorktreeService>();
        return services;
    }

    internal static (DelegationWorktreeService Worktrees, WorktreeManager Manager, ILandingGit Git,
        IRepositoryMutationLease Leases) CreateGitGraph(GitSettings settings,
        Antiphon.Server.Infrastructure.Data.AppDbContext? db = null)
    {
        var git = new LandingGit();
        var leases = new RepositoryMutationLease(git);
        var guarded = new GuardedWorktreeRemoval(git, leases, new TestRemovalEvidence(db));
        var manager = new WorktreeManager(Options.Create(settings), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorktreeManager>.Instance, guarded, leases, git);
        var worktrees = new DelegationWorktreeService(manager,
            new GitService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GitService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(Microsoft.Extensions.Logging.Abstractions.NullLogger<GitWorkspaceService>.Instance),
            leases, git);
        return (worktrees, manager, git, leases);
    }

    private sealed class TestRemovalEvidence(Antiphon.Server.Infrastructure.Data.AppDbContext? database) : IWorktreeRemovalEvidence
    {
        public async Task<Antiphon.Server.Domain.Entities.AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct)
        {
            if (database is null) return null;
            await using var fresh = new Antiphon.Server.Infrastructure.Data.AppDbContext(
                TestDbFixture.CreateDbContextOptions(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetConnectionString(database.Database)));
            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(
                fresh.AgentTaskLandings, o => o.Id == operationId, ct);
        }
    }
}

using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0297: the contract every hand-built dispatcher <c>ServiceCollection</c> leans on. Logging,
/// a clock and <see cref="DelegationTestServices.AddDelegationWorktreeGraph"/> are enough to
/// construct <see cref="DelegationWorktreeService"/>; nothing else may be required, or the next
/// harness clone goes red the way eight of them did when <see cref="GitWorkspaceService"/> became
/// a constructor dependency.
/// </summary>
[Category("Unit")]
public sealed class DelegationTestServicesTests
{
    [Test]
    public async Task Logging_clock_and_helper_resolve_the_whole_worktree_graph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDelegationWorktreeGraph();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<GitWorkspaceService>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IWorktreeManager>()
            .ShouldBeOfType<Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        scope.ServiceProvider.GetRequiredService<IGitService>()
            .ShouldBeOfType<Antiphon.Server.Infrastructure.Git.GitService>();
    }

    [Test]
    public async Task Helper_carries_the_suite_git_settings()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "antiphon-0297-helper-wt");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = basePath });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<GitSettings>>().Value.WorktreeBasePath.ShouldBe(basePath);
    }

    [Test]
    public void Helper_is_TryAdd_so_a_fake_worktree_manager_and_a_prior_one_liner_stand()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IWorktreeManager>(new BridgeQueueHarness.NoWorktreeManager());
        services.AddSingleton<GitWorkspaceService>();

        services.AddDelegationWorktreeGraph();

        services.Count(d => d.ServiceType == typeof(GitWorkspaceService)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IWorktreeManager)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(DelegationWorktreeService)).ShouldBe(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWorktreeManager>().ShouldBeOfType<BridgeQueueHarness.NoWorktreeManager>();
    }

    [Test]
    public void Git_workspace_only_helper_registers_one_singleton_and_no_worktree_graph()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddGitWorkspaceService();
        services.AddGitWorkspaceService();

        services.Count(d => d.ServiceType == typeof(GitWorkspaceService)).ShouldBe(1);
        services.ShouldNotContain(d => d.ServiceType == typeof(DelegationWorktreeService));
        services.ShouldNotContain(d => d.ServiceType == typeof(IWorktreeManager));
    }
}

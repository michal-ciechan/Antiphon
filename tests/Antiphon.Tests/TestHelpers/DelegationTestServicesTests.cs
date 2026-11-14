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
    [Arguments(true)]
    [Arguments(false)]
    public void Managed_root_seam_preserves_caller_paths_without_creating_them(bool absolute)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // Deliberately different from the settings supplied to the call below.
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = "other-root" });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
        var parent = absolute ? Path.GetTempPath() : AppContext.BaseDirectory;
        var leaf = "C0824-MiXeD-" + Guid.NewGuid().ToString("N");
        var expected = Path.Combine(parent, leaf) + Path.DirectorySeparatorChar;
        var configured = Path.Combine(absolute ? parent : ".", "unused", "..", leaf)
            + Path.AltDirectorySeparatorChar;
        Directory.Exists(expected).ShouldBeFalse();

        var actual = service.ResolveWorktreeRoot(new GitSettings { WorktreeBasePath = configured });

        actual.ShouldBe(expected, "keep caller settings, base directory, case, dot segments and trailing separator semantics");
        Directory.Exists(expected).ShouldBeFalse("resolution must retain create=false");
    }

    [Test]
    public void Managed_root_seam_preserves_invalid_path_failure_without_fallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = "valid-service-root" });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();

        Should.Throw<ArgumentException>(() => service.ResolveWorktreeRoot(
            new GitSettings { WorktreeBasePath = "invalid\0caller-root" }));
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    public void Managed_root_seam_preserves_missing_configuration_refusal(string? configured)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDelegationWorktreeGraph();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>();

        Should.Throw<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
            service.ResolveWorktreeRoot(new GitSettings { WorktreeBasePath = configured! }));
    }

    [Test]
    public void C443_DiagnosticRegistrationPreservesOverride()
    {
        var diagnostics = System.Reflection.DispatchProxy.Create<IWorktreeLockDiagnostics, FailOnUseProxy>();
        var probe = System.Reflection.DispatchProxy.Create<IWorktreeDeleteAccessProbe, FailOnUseProxy>();
        var journal = System.Reflection.DispatchProxy.Create<IWorktreeCleanupJournal, FailOnUseProxy>();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(TimeProvider.System);
        services.AddSingleton(diagnostics); services.AddSingleton(probe); services.AddSingleton(journal);
        services.AddDelegationWorktreeGraph(); services.AddDelegationWorktreeGraph();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IWorktreeLockDiagnostics>().ShouldBeSameAs(diagnostics);
        provider.GetRequiredService<IWorktreeDeleteAccessProbe>().ShouldBeSameAs(probe);
        provider.GetRequiredService<IWorktreeCleanupJournal>().ShouldBeSameAs(journal);
        foreach (var type in new[] { typeof(IWorktreeLockDiagnostics), typeof(IWorktreeDeleteAccessProbe), typeof(IWorktreeCleanupJournal) })
            services.Count(d => d.ServiceType == type).ShouldBe(1);
        provider.GetRequiredService<IWorktreeManager>().ShouldNotBeNull();
        var direct = DelegationTestServices.CreateGitGraph(new(), diagnostics: diagnostics, probe: probe, cleanupJournal: journal);
        direct.Diagnostics.ShouldBeSameAs(diagnostics); direct.Probe.ShouldBeSameAs(probe); direct.Journal.ShouldBeSameAs(journal);
    }
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
        scope.ServiceProvider.GetRequiredService<ILandingGit>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IRepositoryMutationLease>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IWorktreeCleanupJournal>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IWorktreeDeleteAccessProbe>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IWorktreeLockDiagnostics>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<AgentTaskLandingState>().ShouldNotBeNull();
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

    public class FailOnUseProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A pure composition test must not invoke external I/O: " + targetMethod?.Name);
    }
}

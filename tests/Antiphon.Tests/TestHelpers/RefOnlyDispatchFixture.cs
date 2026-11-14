using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Application;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real dispatcher/claim/preparer and scratch Git; only the remote transport and launch sink are fake.</summary>
internal sealed class RefOnlyDispatchFixture : IAsyncDisposable
{
    public RunnerSettlementSyncTests.SyncWorld Git { get; private init; } = null!;
    public RemoteWorkspacePreparerTests.Rig Rig { get; private init; } = null!;
    public MutableMonitor<RefOnlyDesktopSettings> Gate { get; private init; } = null!;
    public string Root => Path.Combine(Git.Root, "managed");

    public static async Task<RefOnlyDispatchFixture> CreateAsync(bool enabled = true)
    {
        var git = await RunnerSettlementSyncTests.SyncWorld.CreateAsync(pushBranch: false);
        var monitor = new MutableMonitor<RefOnlyDesktopSettings>(new() { Enabled = enabled, Roles = "Plan" });
        try
        {
            var rig = await RemoteWorkspacePreparerTests.Rig.StartAsync(configure: services =>
            {
                services.AddSingleton<IRepositoryMutationLease>(git.Leases);
                services.AddSingleton<ILandingGit>(git.Git);
                services.AddSingleton<ITaskProgressGit>(git.Git);
                services.AddSingleton(Options.Create(new GitSettings { WorktreeBasePath = Path.Combine(git.Root, "managed") }));
                services.AddSingleton<IOptionsMonitor<RefOnlyDesktopSettings>>(monitor);
            });
            monitor.CurrentValue.RunnerIds = [rig.RunnerId];
            rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
            return new() { Git = git, Rig = rig, Gate = monitor };
        }
        catch { await git.DisposeAsync(); throw; }
    }

    public Task<Guid> SeedAsync(Action<AgentTask>? change = null) => Rig.SeedAsync(t =>
    {
        t.RepoPath = Git.Desktop; t.WorkingDirectory = Git.Desktop;
        t.WorktreePath = null; t.WorktreeBranch = null; t.Role = AgentTaskRole.Plan;
        t.WorktreeBaseRequestedRef = Git.Baseline; t.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target;
        change?.Invoke(t);
    });

    public async Task<AgentTask> PrepareAsync(Guid id)
    {
        await Rig.TickAsync();
        var request = await Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var path = "/runner/ref-only/" + id.ToString("N");
        await Rig.Peer.EmitAsync(RemoteWorkspacePreparerTests.MirrorResult(request, path));
        await Rig.Preparer.WhenIdleAsync();
        return await Rig.ReadTaskAsync(id);
    }

    public async Task<AgentTask> LaunchAsync(Guid id)
    {
        await PrepareAsync(id);
        await Rig.TickAsync();
        var task = await Rig.ReadTaskAsync(id);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        return task;
    }

    public async ValueTask DisposeAsync()
    {
        await Rig.DisposeAsync();
        await Git.DisposeAsync();
    }
}

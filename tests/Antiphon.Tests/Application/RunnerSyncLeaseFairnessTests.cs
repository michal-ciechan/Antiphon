using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using LeaseBusySignal = Antiphon.Tests.Application.RunnerSettlementSyncTests.LeaseBusySignal;
using SyncWorld = Antiphon.Tests.Application.RunnerSettlementSyncTests.SyncWorld;
using UnusedDirectory = Antiphon.Tests.Application.RunnerSettlementSyncTests.UnusedDirectory;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1479. An ordinary runner settlement sync that finds the repository mutation lease busy is a
/// real waiter in the CARD-0672 D-2 turnstile (<see cref="RepositoryLeaseWaiters"/>), so a land
/// about to acquire the lease yields to it at admission. The producer is the real
/// <see cref="RemoteWorkspaceService"/> against real git; no test registers a waiter by hand. The
/// clock is fake, so slices and budgets are advanced, never slept.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class RunnerSyncLeaseFairnessTests
{
    /// <summary>
    /// V-1 (the red witness). A real sync waits on a held R, registers <c>worktree-settlement</c>
    /// without any test calling <see cref="RepositoryLeaseWaiters.Register"/>, and the next real
    /// land admission on that repository stands aside with the yield code before any Git
    /// mutation. After the holder lets go the sync fast-forwards, the waiter is gone and the land
    /// completes. The base leaves the snapshot empty and never yields.
    /// </summary>
    [Test]
    public async Task Busy_sync_makes_next_land_yield()
    {
        await using var w = await LandWorld.CreateAsync();
        var s = await w.RunnerPushAsync("work.txt", "runner");
        await w.FetchSourceAsync();
        var held = await w.Leases.TryAcquireAsync(w.Repository, CancellationToken.None);
        held.ShouldNotBeNull();
        var busy = new LeaseBusySignal();
        var sync = w.Service(busy.Observe).SyncAsync(w.SyncTask, CancellationToken.None);
        (await Task.WhenAny(busy.First, sync)).ShouldBe(busy.First, "the sync must wait for the held lease");

        // The sync is a registered worktree-settlement waiter on the land's repository.
        var waiter = w.Waiters.Snapshot(w.Common).ShouldHaveSingleItem("the waiting sync registers itself; nothing else does");
        waiter.TaskId.ShouldBe(w.SyncTaskId);
        waiter.Purpose.ShouldBe(RepositoryLeasePurposes.WorktreeSettlement);
        waiter.VerificationCompatible.ShouldBeFalse("a sync has no compatible preflight; windows stay exclusive");

        // The land stands aside for it: Held with the yield code, no Git mutation, the lease untouched.
        w.Harness.Fixture.Git.Trace.Clear();
        (await w.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        var request = await w.RequestAsync();
        request.HoldReasonCode.ShouldBe(AgentTaskLandService.LeaseYieldedToDispatchCode);
        request.HoldDetail.ShouldNotBeNull();
        request.HoldDetail.ShouldContain(DelegationReportFormatter.Short(w.SyncTaskId));
        request.HoldDetail.ShouldContain("(" + RepositoryLeasePurposes.WorktreeSettlement + ")");
        w.Harness.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("push")
            || a.Contains("worktree") || a.Contains("--ff-only"));
        (await w.EventsAsync(AgentTaskEventType.Held)).ShouldHaveSingleItem();

        // The holder lets go inside the budget: the sync takes the lease, fast-forwards, and leaves no waiter.
        await held.DisposeAsync();
        var result = await w.DriveAsync(sync);
        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "reason=" + result.Reason);
        result.DesktopAfterSha.ShouldBe(s);
        (await w.SyncHeadAsync()).ShouldBe(s);
        w.Waiters.Any(w.Common).ShouldBeFalse("the acquired sync clears its waiter");
        w.Waiters.IsEmpty.ShouldBeTrue();

        // The land got its turn.
        w.Clock.Advance(TimeSpan.FromSeconds(5));
        (await w.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);
        await w.AssertLandedAsync();
    }

    /// <summary>
    /// A <see cref="LandingSafetyHarness"/> repository with one land task (the fixture's) and one
    /// runner-bound sync task registered in the SAME repository, sharing one waiter registry and
    /// one fake clock. The sync task's branch is pushed to the fixture remote; a separate clone
    /// stands in for the runner, so the desktop has never seen its commits before the sync.
    /// </summary>
    private sealed class LandWorld : IAsyncDisposable
    {
        public LandingSafetyHarness Harness { get; private set; } = null!;
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        public RepositoryLeaseWaiters Waiters { get; } = new();
        public Guid SyncTaskId { get; } = Guid.NewGuid();
        public string SyncBranch => "feat/card-task-" + SyncTaskId.ToString("N")[..8];
        public string SyncFullRef => "refs/heads/" + SyncBranch;
        public string Repository => Harness.Fixture.Repository;
        public string SyncWorktree => Path.Combine(Harness.Fixture.Root, "sync-worktree");
        public string Runner => Path.Combine(Harness.Fixture.Root, "sync-runner");
        public string Common { get; private set; } = "";
        public string Baseline { get; private set; } = "";
        public AgentTask SyncTask { get; private set; } = null!;
        public IRepositoryMutationLease Leases => Harness.Services.GetRequiredService<IRepositoryMutationLease>();
        private TaskProgressGit _git = null!;
        private bool _runnerCloned;

        public static async Task<LandWorld> CreateAsync(int maxYieldSeconds = 90)
        {
            var w = new LandWorld();
            var h = new LandingSafetyHarness { Clock = w.Clock, LandSettings = new() { LandYieldToDispatchMaxSeconds = maxYieldSeconds } };
            h.ConfigureServices = services => services.AddSingleton(w.Waiters);
            w.Harness = h;
            await h.InitializeAsync();
            await h.AddSourceAsync();
            await h.RequestAsync();

            // The sync task's checkout: registered in the land's repository, on its own branch, pushed.
            await h.Fixture.RequiredAsync(w.Repository, "worktree", "add", "-b", w.SyncBranch, w.SyncWorktree, "master");
            await h.Fixture.RequiredAsync(w.SyncWorktree, "push", "origin", w.SyncBranch);
            w._git = new TaskProgressGit(w.Leases);
            var repo = await w._git.CanonicalDirectoryAsync(w.Repository, CancellationToken.None);
            w.Common = await w._git.CommonDirectoryAsync(repo, CancellationToken.None);
            var worktree = await w._git.CanonicalDirectoryAsync(w.SyncWorktree, CancellationToken.None);
            w.Baseline = (await w._git.RevParseCommitAsync(worktree, "HEAD", CancellationToken.None)).Sha!;
            var remote = await w._git.ObserveExactRefAsync(repo, w.SyncFullRef, null, w.SyncTaskId, CancellationToken.None);
            remote.EndpointFingerprint.ShouldNotBeNull();
            var snapshot = new ProgressBaselineSnapshot(1, DateTime.UtcNow, DateTime.UtcNow,
                new ProgressSourceBaseline(repo, w.Common, w.SyncTaskId, worktree, w.SyncFullRef, w.Baseline,
                    new ProgressRemoteBaseline(remote.State, remote.Sha, remote.EndpointFingerprint, remote.Reason)),
                null);
            w.SyncTask = new AgentTask
            {
                Id = w.SyncTaskId,
                Workspace = WorkspaceMode.Worktree,
                Role = AgentTaskRole.Code,
                RunnerId = "server2",
                RepoPath = repo,
                WorkingDirectory = repo,
                WorktreePath = worktree,
                WorktreeBranch = w.SyncBranch,
                WorktreeBaseSha = w.Baseline,
                RemoteWorktreePath = "/work/worktrees/task-" + w.SyncTaskId.ToString("N")[..8],
                ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot),
            };
            return w;
        }

        /// <summary>A fresh sync service, as each dispatcher sweep's scope builds one; the registry and clock are shared.</summary>
        public RemoteWorkspaceService Service(Action? leaseBusy = null) =>
            new(new UnusedDirectory(), _git, NullLogger<RemoteWorkspaceService>.Instance, _git, Leases, leaseWaiters: Waiters)
            {
                Clock = Clock,
                LeaseBusyObserved = leaseBusy,
            };

        /// <summary>Commit and push on the runner clone only. Returns the pushed commit.</summary>
        public async Task<string> RunnerPushAsync(string file, string content)
        {
            if (!_runnerCloned)
            {
                await Harness.Fixture.RequiredAsync(Harness.Fixture.Root, "clone", "-b", SyncBranch, Harness.Fixture.Remote, Runner);
                _runnerCloned = true;
            }
            await File.WriteAllTextAsync(Path.Combine(Runner, file), content);
            await Harness.Fixture.RequiredAsync(Runner, "add", file);
            await Harness.Fixture.RequiredAsync(Runner, "commit", "-m", "runner " + file);
            await Harness.Fixture.RequiredAsync(Runner, "push", "origin", "HEAD:" + SyncFullRef);
            return (await Harness.Fixture.RequiredAsync(Runner, "rev-parse", "HEAD")).Trim();
        }

        /// <summary>Makes the pushed objects local, so the sync's only busy boundary is the mutation acquire.</summary>
        public Task FetchSourceAsync() => Harness.Fixture.RequiredAsync(Repository, "fetch", "--no-tags", "origin", SyncFullRef);

        /// <summary>Advances the fake clock one retry interval at a time until the sync returns, inside its budget.</summary>
        public async Task<RemoteSettlementSyncResult> DriveAsync(Task<RemoteSettlementSyncResult> sync)
        {
            var service = Service();
            var advanced = TimeSpan.Zero;
            while (!sync.IsCompleted && advanced + service.LeaseRetryInterval < service.SyncBudget)
            {
                Clock.Advance(service.LeaseRetryInterval);
                advanced += service.LeaseRetryInterval;
                await Task.WhenAny(sync, Task.Delay(50));
            }
            return await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
        }

        public async Task<string> SyncHeadAsync() => (await Harness.Fixture.RequiredAsync(SyncWorktree, "rev-parse", "HEAD")).Trim();

        public async Task<AgentTaskLandRequest> RequestAsync()
        {
            await using var db = Harness.CreateContext();
            return await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == Harness.Fixture.TaskId);
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(AgentTaskEventType type)
        {
            await using var db = Harness.CreateContext();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == Harness.Fixture.TaskId && e.Type == type)
                .OrderBy(e => e.At).ThenBy(e => e.Id)
                .ToListAsync();
        }

        public async Task AssertLandedAsync()
        {
            new AgentTaskLandingState().HasPublication((await Harness.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue();
            await Harness.Fixture.AssertRemoteSourceAsync();
        }

        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }
}

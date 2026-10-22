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
[ParallelLimiter<ProcessSpawnLimit>]
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
        await held!.DisposeAsync();
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
    /// V-2. The pushed objects are not local, so the sync's first busy boundary is the observation
    /// fetch. The same worktree-settlement waiter appears there, before any fetch or checkout
    /// mutation, and survives the slice ending in <c>runner_sync_lease_waiting</c>.
    /// </summary>
    [Test]
    public async Task Fetch_wait_registers_before_objects_are_local()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner");
        (await world.HasObjectAsync(s)).ShouldBeFalse();
        await using var held = await world.Leases.TryAcquireAsync(world.Desktop, CancellationToken.None);
        held.ShouldNotBeNull();
        world.Git.Clear();
        var waiters = new RepositoryLeaseWaiters();
        var clock = new FakeTimeProvider();
        var busy = new LeaseBusySignal();
        var service = Service(world, waiters, clock, busy.Observe);

        var sync = service.SyncAsync(world.Task, CancellationToken.None);
        (await Task.WhenAny(busy.First, sync)).ShouldBe(busy.First, "the observation needs the lease to fetch");
        var waiter = waiters.Snapshot(CommonOf(world)).ShouldHaveSingleItem("the fetch wait registers the sync");
        waiter.TaskId.ShouldBe(world.TaskId);
        waiter.Purpose.ShouldBe(RepositoryLeasePurposes.WorktreeSettlement);
        waiter.VerificationCompatible.ShouldBeFalse();
        waiter.Since.ShouldBe(clock.GetUtcNow());

        clock.Advance(service.LeaseWaitSlice);
        var result = await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
        result.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        result.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseWaiting);
        waiters.Snapshot(CommonOf(world)).ShouldHaveSingleItem("a still-waiting sync keeps its place").Since.ShouldBe(waiter.Since);
        world.Git.Commands.ShouldNotContain(c => c.StartsWith("fetch", StringComparison.Ordinal)
            || c.Contains("merge --ff-only", StringComparison.Ordinal) || c.StartsWith("update-ref", StringComparison.Ordinal));
        (await world.HasObjectAsync(s)).ShouldBeFalse();
        (await world.HeadAsync()).ShouldBe(world.Baseline);
    }

    /// <summary>
    /// V-3. One attempt per dispatcher sweep, each on a fresh service as each sweep's scope builds
    /// it. Two attempts return <c>runner_sync_lease_waiting</c>; one waiter with the original
    /// <c>Since</c> survives between them and the land yields meanwhile. After the holder lets go
    /// the third attempt succeeds, removes the waiter, and the land lands.
    /// </summary>
    [Test]
    public async Task Waiter_survives_slices_with_original_since()
    {
        await using var w = await LandWorld.CreateAsync();
        var s = await w.RunnerPushAsync("work.txt", "runner");
        await w.FetchSourceAsync();
        var held = await w.Leases.TryAcquireAsync(w.Repository, CancellationToken.None);
        held.ShouldNotBeNull();

        async Task SweepAsync(string row)
        {
            var busy = new LeaseBusySignal();
            var service = w.Service(busy.Observe);
            var sync = service.SyncAsync(w.SyncTask, CancellationToken.None);
            (await Task.WhenAny(busy.First, sync)).ShouldBe(busy.First, row + ": the sync must meet the busy lease");
            w.Clock.Advance(service.LeaseWaitSlice);
            var result = await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
            result.State.ShouldBe(RemoteSettlementSyncState.Unavailable, row);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseWaiting, row);
        }

        var since = w.Clock.GetUtcNow();
        await SweepAsync("sweep 1");
        var first = w.Waiters.Snapshot(w.Common).ShouldHaveSingleItem("the waiting sync keeps its place between sweeps");
        first.TaskId.ShouldBe(w.SyncTaskId);
        first.Since.ShouldBe(since);
        (await w.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        (await w.RequestAsync()).HoldReasonCode.ShouldBe(AgentTaskLandService.LeaseYieldedToDispatchCode);

        w.Clock.Advance(TimeSpan.FromSeconds(50));
        await SweepAsync("sweep 2");
        var second = w.Waiters.Snapshot(w.Common).ShouldHaveSingleItem("one waiter per task, not one per sweep");
        second.Since.ShouldBe(since, "a repeat registration keeps the original Since");
        (await w.Harness.RunAsync()).ShouldBe(LandRunResult.Held);
        (await w.EventsAsync(AgentTaskEventType.Held)).ShouldHaveSingleItem("one yield episode across both sweeps");

        await held!.DisposeAsync();
        var result = await w.Service().SyncAsync(w.SyncTask, CancellationToken.None);
        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "reason=" + result.Reason);
        result.DesktopAfterSha.ShouldBe(s);
        w.Waiters.Any(w.Common).ShouldBeFalse("the synchronized sync leaves no waiter");

        w.Clock.Advance(TimeSpan.FromSeconds(5));
        (await w.Harness.RunAsync()).ShouldBe(LandRunResult.Complete);
        await w.AssertLandedAsync();
    }

    /// <summary>
    /// V-4. Every way an attempt that registered can end leaves no fairness ghost: acquisition
    /// after the wait, a refusal past the fetch, the spent budget, an exception under the lease,
    /// the caller's own cancellation during the wait, and a re-hand of a task that is no longer
    /// eligible. One method, an internal table.
    /// </summary>
    [Test]
    public async Task Every_terminal_exit_clears_waiter()
    {
        foreach (var arm in new[] { "acquired", "refused", "exhausted", "exception", "cancellation", "changed-eligibility" })
        {
            await using var world = await SyncWorld.CreateAsync();
            var s = await world.RunnerPushAsync("work.txt", arm);
            if (arm is "acquired" or "exception")
                await world.RunAsync(world.Desktop, "fetch", "--no-tags", "origin", world.FullRef);
            await using var held = await world.Leases.TryAcquireAsync(world.Desktop, CancellationToken.None);
            held.ShouldNotBeNull(arm);
            var waiters = new RepositoryLeaseWaiters();
            var clock = new FakeTimeProvider();
            var busy = new LeaseBusySignal();
            var git = arm == "exception" ? new ThrowingGit(world.Leases) { ThrowOn = a => a.Contains("--ff-only") } : null;
            var service = Service(world, waiters, clock, busy.Observe, git);
            using var cancel = new CancellationTokenSource();
            string[]? tips = arm == "refused" ? new[] { new string('a', 40) } : null;
            var common = CommonOf(world);

            var sync = service.SyncAsync(world.Task, cancel.Token, tips);
            (await Task.WhenAny(busy.First, sync)).ShouldBe(busy.First, arm + ": the sync must meet the busy lease");
            waiters.Snapshot(common).ShouldHaveSingleItem(arm + ": registered before the exit").TaskId.ShouldBe(world.TaskId);

            switch (arm)
            {
                case "acquired":
                    await held!.DisposeAsync();
                    var acquired = await DriveAsync(clock, service, sync);
                    acquired.State.ShouldBe(RemoteSettlementSyncState.Synchronized, arm + " reason=" + acquired.Reason);
                    break;
                case "refused":
                    await held!.DisposeAsync();
                    var refused = await DriveAsync(clock, service, sync);
                    refused.State.ShouldBe(RemoteSettlementSyncState.Refused, arm + " reason=" + refused.Reason);
                    refused.Reason.ShouldBe(RemoteSettlementSyncReasons.TipNotReported, arm);
                    break;
                case "exhausted":
                    clock.Advance(service.SyncBudget);
                    (await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard)).Reason
                        .ShouldBe(RemoteSettlementSyncReasons.LeaseBusy, arm);
                    break;
                case "exception":
                    await held!.DisposeAsync();
                    var faulted = await DriveAsync(clock, service, sync);
                    git!.Thrown.ShouldBeTrue(arm + ": the injected fault must fire under the lease");
                    faulted.State.ShouldBe(RemoteSettlementSyncState.Unavailable, arm + " reason=" + faulted.Reason);
                    faulted.Reason.ShouldBe(RemoteSettlementSyncReasons.InspectionUnavailable, arm);
                    break;
                case "cancellation":
                    cancel.Cancel();
                    var canceled = false;
                    try { await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard); }
                    catch (OperationCanceledException) { canceled = true; }
                    canceled.ShouldBeTrue(arm + ": the caller's own cancellation propagates out of the wait");
                    break;
                case "changed-eligibility":
                    clock.Advance(service.LeaseWaitSlice);
                    (await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard)).Reason
                        .ShouldBe(RemoteSettlementSyncReasons.LeaseWaiting, arm);
                    waiters.Any(common).ShouldBeTrue(arm + ": the waiting sync keeps its place for the re-hand");
                    var rehand = await Service(world, waiters, clock)
                        .SyncAsync(world.TaskWith(t => t.RunnerId = null), CancellationToken.None);
                    rehand.State.ShouldBe(RemoteSettlementSyncState.NotApplicable, arm);
                    break;
            }

            waiters.IsEmpty.ShouldBeTrue(arm + ": no fairness ghost after the exit");
        }
    }

    /// <summary>
    /// V-5. The durable debt recovery's single attempt returns at once on a busy lease and
    /// registers nothing at either boundary: a five-minute backoff is not runnable demand, so a
    /// land must not spend its yield budget on it. The recovery service's Pending/retry mapping
    /// of that result is unchanged by this slice.
    /// </summary>
    [Test]
    public async Task Debt_backoff_leaves_no_waiter()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "recorded");
        var debt = DebtFor(world, s);
        var waiters = new RepositoryLeaseWaiters();
        var clock = new FakeTimeProvider();
        var busy = new LeaseBusySignal();
        await using var held = await world.Leases.TryAcquireAsync(world.Desktop, CancellationToken.None);
        held.ShouldNotBeNull();

        foreach (var boundary in new[] { "observation", "acquire" })
        {
            if (boundary == "acquire")
                await world.RunAsync(world.Desktop, "fetch", "--no-tags", "origin", world.FullRef);
            var result = await Service(world, waiters, clock, busy.Observe)
                .SyncSettledAsync(world.Task, debt, CancellationToken.None)
                .WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
            result.State.ShouldBe(RemoteSettlementSyncState.Unavailable, boundary);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy, boundary + ": a busy single attempt returns at once");
            busy.Count.ShouldBe(0, boundary + ": no wait");
            waiters.IsEmpty.ShouldBeTrue(boundary + ": a backing-off debt is not runnable demand");
        }
        (await world.HeadAsync()).ShouldBe(world.Baseline);

        await held!.DisposeAsync();
        var synced = await Service(world, waiters, clock).SyncSettledAsync(world.Task, debt, CancellationToken.None);
        synced.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "reason=" + synced.Reason);
        synced.DesktopAfterSha.ShouldBe(s);
        waiters.IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// V-6. A real live verification window keeps refusing an ordinary sync after R itself is
    /// released: the sync has no compatible preflight, so for its whole budget it modifies neither
    /// the source checkout, its branch nor the target, and its spent attempt leaves no waiter.
    /// Closing the window lets the next sync succeed.
    /// </summary>
    [Test]
    public async Task Verification_window_remains_exclusive()
    {
        await using var w = await LandWorld.CreateAsync();
        var s = await w.RunnerPushAsync("work.txt", "runner");
        var f = w.Harness.Fixture;
        var master = (await f.RequiredAsync(w.Repository, "rev-parse", f.TargetRef)).Trim();
        await using var execution = await new LandExecutionLease(f.Git).TryAcquireAsync(w.Repository, CancellationToken.None);
        execution.ShouldNotBeNull();
        var mutation = await w.Leases.TryAcquireAsync(w.Repository, CancellationToken.None);
        mutation.ShouldNotBeNull();
        var identity = new RepositoryVerificationIdentity(f.TaskId, Guid.NewGuid(), 1, Guid.NewGuid(),
            f.SeedSha, f.Source, f.Repository, f.SourceRef, f.TargetRef);
        var window = await w.Leases.OpenVerificationWindowAsync(mutation!, execution!, identity, CancellationToken.None);
        window.ShouldNotBeNull();
        await mutation!.DisposeAsync();

        var busy = new LeaseBusySignal();
        var service = w.Service(busy.Observe);
        var sync = service.SyncAsync(w.SyncTask, CancellationToken.None);
        (await Task.WhenAny(busy.First, sync)).ShouldBe(busy.First, "the live window refuses an ordinary sync with R released");
        w.Waiters.Snapshot(w.Common).ShouldHaveSingleItem().Purpose.ShouldBe(RepositoryLeasePurposes.WorktreeSettlement);
        w.Clock.Advance(service.SyncBudget);
        var refused = await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
        refused.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        refused.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        (await w.SyncHeadAsync()).ShouldBe(w.Baseline, "no source checkout mutation under a window");
        (await f.RequiredAsync(w.Repository, "rev-parse", w.SyncFullRef)).Trim().ShouldBe(w.Baseline, "no source branch mutation under a window");
        (await f.RequiredAsync(w.Repository, "rev-parse", f.TargetRef)).Trim().ShouldBe(master, "no target mutation under a window");
        w.Waiters.IsEmpty.ShouldBeTrue("the spent sync leaves no waiter");

        await window!.DisposeAsync();
        var synced = await w.Service().SyncAsync(w.SyncTask, CancellationToken.None);
        synced.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "reason=" + synced.Reason);
        (await w.SyncHeadAsync()).ShouldBe(s);
        w.Waiters.IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// V-7. The booted Program's own scoped <see cref="RemoteWorkspaceService"/>, driven against a
    /// lease its own <see cref="IRepositoryMutationLease"/> holds, registers in the root
    /// <see cref="RepositoryLeaseWaiters"/> singleton that the scoped land service yields to; the
    /// root decides Yield for that repository, and the waiter is gone once the sync completes.
    /// Real clock: the production service keeps its 1 s retry.
    /// </summary>
    [Test]
    public async Task Production_services_share_the_waiter_registry()
    {
        await using var factory = new AntiphonWebAppFactory();
        var root = factory.Services.GetRequiredService<RepositoryLeaseWaiters>();
        var leases = factory.Services.GetRequiredService<IRepositoryMutationLease>();
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner");
        var common = CommonOf(world);
        await using var held = await leases.TryAcquireAsync(world.Desktop, CancellationToken.None);
        held.ShouldNotBeNull();

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RemoteWorkspaceService>();
        var land = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        Field<RepositoryLeaseWaiters>(land).ShouldBeSameAs(root, "the land yields to the registry the sync registers in");

        var sync = service.SyncAsync(world.Task, CancellationToken.None, [s]);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!root.Any(common) && !sync.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        var early = sync.IsCompleted ? (await sync).Reason : null;
        early.ShouldBeNull("the sync must still be waiting when the registration is read");
        var waiter = root.Snapshot(common).ShouldHaveSingleItem("the production sync registers in the shared root registry");
        waiter.TaskId.ShouldBe(world.TaskId);
        waiter.Purpose.ShouldBe(RepositoryLeasePurposes.WorktreeSettlement);
        waiter.VerificationCompatible.ShouldBeFalse();
        var requestId = Guid.NewGuid();
        root.DecideYield(common, requestId, DateTimeOffset.UtcNow, 90).Outcome
            .ShouldBe(LandYieldOutcome.Yield, "a land on that repository stands aside");
        root.EndYield(requestId);

        await held!.DisposeAsync();
        var result = await sync.WaitAsync(TimeSpan.FromSeconds(60));
        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "reason=" + result.Reason);
        result.DesktopAfterSha.ShouldBe(s);
        root.Any(common).ShouldBeFalse("the synchronized sync leaves no waiter");
    }

    private static RemoteWorkspaceService Service(SyncWorld world, RepositoryLeaseWaiters waiters, FakeTimeProvider clock,
        Action? leaseBusy = null, TaskProgressGit? git = null) =>
        new(new UnusedDirectory(), git ?? world.Git, NullLogger<RemoteWorkspaceService>.Instance, git ?? world.Git,
            world.Leases, leaseWaiters: waiters)
        {
            Clock = clock,
            LeaseBusyObserved = leaseBusy,
        };

    private static string CommonOf(SyncWorld world) =>
        TaskProgressJson.TryReadBaseline(world.Task.ProgressBaselineJson)!.Primary.CanonicalCommonDirectory;

    /// <summary>Advances the fake clock one retry interval at a time until the sync returns, inside its budget.</summary>
    private static async Task<RemoteSettlementSyncResult> DriveAsync(FakeTimeProvider clock, RemoteWorkspaceService service,
        Task<RemoteSettlementSyncResult> sync)
    {
        var advanced = TimeSpan.Zero;
        while (!sync.IsCompleted && advanced + service.LeaseRetryInterval < service.SyncBudget)
        {
            clock.Advance(service.LeaseRetryInterval);
            advanced += service.LeaseRetryInterval;
            await Task.WhenAny(sync, Task.Delay(50));
        }
        return await sync.WaitAsync(RunnerSettlementSyncTests.SliceReturnGuard);
    }

    private static AgentTaskSyncDebt DebtFor(SyncWorld world, string source)
    {
        var baseline = TaskProgressJson.TryReadBaseline(world.Task.ProgressBaselineJson)!.Primary;
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        return new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(),
            TaskId = world.Task.Id,
            Attempt = world.Task.Attempt,
            SettlementEventId = Guid.NewGuid(),
            RunnerId = world.Task.RunnerId,
            WorktreePath = world.Task.WorktreePath,
            RemoteWorktreePath = world.Task.RemoteWorktreePath,
            RepositoryPath = baseline.CanonicalRepository,
            FullRef = baseline.FullRef,
            BaselineSha = baseline.LocalSha,
            SourceSha = source,
            DesktopBeforeSha = world.Baseline,
            EndpointFingerprint = baseline.Remote.EndpointFingerprint,
            State = AgentTaskSyncDebtState.Pending,
            ReasonCode = RemoteSettlementSyncReasons.LeaseBusy,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static T? Field<T>(object owner) where T : class =>
        (T?)owner.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(T)).GetValue(owner);

    /// <summary>The production Git I/O with one injected fault: the first matching command throws.</summary>
    private sealed class ThrowingGit(IRepositoryMutationLease? leases) : TaskProgressGit(leases)
    {
        public Func<IReadOnlyList<string>, bool>? ThrowOn { get; set; }
        public bool Thrown { get; private set; }

        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (!Thrown && ThrowOn?.Invoke(arguments) == true)
            {
                Thrown = true;
                throw new IOException("c1479_injected_fault");
            }
            return base.RunAsync(repository, arguments, ct);
        }
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

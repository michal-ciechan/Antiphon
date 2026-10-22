using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1427 V-4 / CARD-1428 V-7: the dispatcher's own lease-waiter and inspection-result
/// retention on <see cref="SiblingInspectionFixture"/> (scratch repository, fake clock), with the
/// repository mutation lease replaced by one that refuses a configurable number of acquisitions.
/// </summary>
public partial class DispatcherSiblingInspectionSchedulingTests
{
    /// <summary>
    /// CARD-1427 D-2: a waiter registered by a lease refusal survives the next tick when that tick's
    /// only hold is the pending same-card inspection, and keeps its original Since.
    /// </summary>
    [Test]
    public async Task C1427_inspection_pending_keeps_the_lease_waiter()
    {
        var waiters = new RepositoryLeaseWaiters();
        await using var f = await SiblingInspectionFixture.CreateAsync(
            configure: services => RefuseLease(services, waiters, refusals: int.MaxValue));
        var common = await f.Git.CommonDirectoryAsync(f.Repo.Path, default);

        // Tick 1: no result yet, the inspection is enqueued and completes.
        await f.ObserveAsync();
        waiters.IsEmpty.ShouldBeTrue("nothing was refused the lease yet");

        // Tick 2: the result is ready; the lease refuses the claim; the waiter is registered at T1.
        var t1 = f.Clock.GetUtcNow();
        var refused = await f.TickAsync();
        refused.HeldOnLease.ShouldBe(1);
        var waiter = waiters.Snapshot(common).ShouldHaveSingleItem();
        waiter.TaskId.ShouldBe(f.Task.Id);
        waiter.Since.ShouldBe(t1);

        // Tick 3, 31 s later, inspection-pending: before CARD-1428 the refused attempt consumed the
        // observation; now it is retained, so the pending state is forced here. The waiter is still
        // registered, with the same Since.
        f.Clock.Advance(TimeSpan.FromSeconds(31));
        f.Queue.Invalidate(f.Task.Id);
        var pending = await f.TickAsync();
        pending.HeldOnLease.ShouldBe(0, "an inspection-pending tick is not a lease refusal");
        await using (var read = f.Read())
        {
            var held = await read.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == f.Task.Id && e.Type == AgentTaskEventType.Held)
                .OrderBy(e => e.At).ThenBy(e => e.Id).Select(e => e.Detail).ToListAsync();
            held.Last().ShouldBe(DispatchHoldDetails.SiblingInspectionPending);
        }
        waiters.Snapshot(common).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            w => w.TaskId.ShouldBe(f.Task.Id),
            w => w.Since.ShouldBe(t1, "the waiter keeps its turn and its age across the inspection-pending tick"));
        await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// CARD-1428 D-4: the lease refuses attempt 1 and grants attempt 2. The observation from the
    /// refused attempt is reused: no second inspection runs, and the second tick claims and cuts.
    /// </summary>
    [Test]
    public async Task C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt()
    {
        var waiters = new RepositoryLeaseWaiters();
        await using var f = await SiblingInspectionFixture.CreateAsync(
            configure: services => RefuseLease(services, waiters, refusals: 1));
        var common = await f.Git.CommonDirectoryAsync(f.Repo.Path, default);
        var workerCommands = CountWorkerCommands(f);

        await f.ObserveAsync();
        var inspected = Volatile.Read(ref workerCommands.Value);
        inspected.ShouldBeGreaterThan(0, "one owned inspection observed the card");

        var refused = await f.TickAsync();
        refused.HeldOnLease.ShouldBe(1);
        refused.Dispatched.ShouldBe(0);
        waiters.Snapshot(common).ShouldHaveSingleItem().TaskId.ShouldBe(f.Task.Id);
        f.Queue.Outstanding.ShouldBe(1, "the refused attempt's result is retained");
        f.Queue.InFlight.ShouldBe(0, "no new inspection was enqueued");
        Volatile.Read(ref workerCommands.Value).ShouldBe(inspected, "the refusal did not re-inspect");

        f.Clock.Advance(TimeSpan.FromSeconds(10));
        var granted = await f.TickAsync();
        granted.Dispatched.ShouldBe(1, "the retained observation is claimed and cut on the next attempt");
        granted.HeldOnLease.ShouldBe(0);
        Volatile.Read(ref workerCommands.Value).ShouldBe(inspected, "the inspection ran once in total");
        await using var read = f.Read();
        var task = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.WorktreePath.ShouldNotBeNull();
        task.AgentSessionId.ShouldNotBeNull();
        f.Queue.Outstanding.ShouldBe(0, "a dispatched task's result is released");
        waiters.IsEmpty.ShouldBeTrue("the dispatched task no longer waits");
    }

    /// <summary>
    /// CARD-1428 D-3: the frozen base ref moves between the observation and the locked claim. The
    /// claim is SiblingObservationStale, not a plain NotClaimed: the same intent is re-inspected at
    /// once and the lease waiter keeps its turn and its Since (CARD-1427 D-2). The fresh observation
    /// is then claimed and cut.
    /// </summary>
    [Test]
    public async Task C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter()
    {
        var waiters = new RepositoryLeaseWaiters();
        SiblingInspectionFixture? fixture = null;
        await using var f = await SiblingInspectionFixture.CreateAsync(
            configure: services => RefuseLease(services, waiters, refusals: 1,
                onGranted: () => fixture!.Repo.CommitFileAsync("moved.txt", "the land that moved master")));
        fixture = f;
        var common = await f.Git.CommonDirectoryAsync(f.Repo.Path, default);
        var workerCommands = CountWorkerCommands(f);

        await f.ObserveAsync();
        var inspected = Volatile.Read(ref workerCommands.Value);
        inspected.ShouldBeGreaterThan(0);
        var t1 = f.Clock.GetUtcNow();
        (await f.TickAsync()).HeldOnLease.ShouldBe(1);
        waiters.Snapshot(common).ShouldHaveSingleItem().Since.ShouldBe(t1);

        // The lease is granted; master moves under the claim; the frozen ref no longer matches.
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        var stale = await f.TickAsync();
        stale.Dispatched.ShouldBe(0, "a stale observation never cuts");
        stale.HeldOnLease.ShouldBe(0, "the lease was granted, not refused");
        await using (var read = f.Read())
        {
            var row = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            row.Status.ShouldBe(AgentTaskStatus.Queued); row.WorktreePath.ShouldBeNull(); row.AgentSessionId.ShouldBeNull();
            var held = await read.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == f.Task.Id && e.Type == AgentTaskEventType.Held)
                .OrderBy(e => e.At).ThenBy(e => e.Id).Select(e => e.Detail).ToListAsync();
            held.Last().ShouldBe(DispatchHoldDetails.SiblingInspectionPending, "the stale claim is a pending-inspection hold");
        }
        f.Queue.Outstanding.ShouldBe(1, "the same intent was re-enqueued at once");
        waiters.Snapshot(common).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            w => w.TaskId.ShouldBe(f.Task.Id),
            w => w.Since.ShouldBe(t1, "the waiter keeps its turn across the stale-at-claim tick"));

        await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Volatile.Read(ref workerCommands.Value).ShouldBeGreaterThan(inspected, "a fresh owned inspection ran");
        f.Queue.InFlight.ShouldBe(0);
        f.Queue.Outstanding.ShouldBe(1, "exactly one fresh observation is retained");
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        var cut = await f.TickAsync();
        cut.Dispatched.ShouldBe(1, "the fresh observation is claimed and cut");
        await using var after = f.Read();
        var dispatched = await after.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason);
        (await ScratchGitRepo.GitInAsync(dispatched.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe((await f.Repo.GitReadAsync("rev-parse", "master")).Trim(), "cut from the moved master");
        waiters.IsEmpty.ShouldBeTrue();
    }

    private sealed class Counter { public int Value; }

    /// <summary>
    /// Git commands issued by the owned inspection workers: the claim's revalidation and the cut run
    /// on the tick with no worker active, so they are not counted. Independent of how many commands
    /// one inspection issues.
    /// </summary>
    private static Counter CountWorkerCommands(SiblingInspectionFixture f)
    {
        var counter = new Counter();
        f.Git.Before = (_, _) =>
        {
            if (f.Queue.Active > 0) Interlocked.Increment(ref counter.Value);
            return Task.FromResult<LandingGitResult?>(null);
        };
        return counter;
    }

    private static void RefuseLease(IServiceCollection services, RepositoryLeaseWaiters waiters, int refusals,
        Func<Task>? onGranted = null)
    {
        services.RemoveAll<IRepositoryMutationLease>();
        services.AddSingleton<IRepositoryMutationLease>(sp =>
            new RefusingRepositoryLease(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()), refusals, onGranted));
        services.AddSingleton(waiters);
    }
}

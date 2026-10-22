using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
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

        // Tick 3, 31 s later: the refused attempt's observation is gone, so this tick is
        // inspection-pending. The waiter is still registered, with the same Since.
        f.Clock.Advance(TimeSpan.FromSeconds(31));
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

    private static void RefuseLease(IServiceCollection services, RepositoryLeaseWaiters waiters, int refusals)
    {
        services.RemoveAll<IRepositoryMutationLease>();
        services.AddSingleton<IRepositoryMutationLease>(sp =>
            new RefusingRepositoryLease(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()), refusals));
        services.AddSingleton(waiters);
    }
}

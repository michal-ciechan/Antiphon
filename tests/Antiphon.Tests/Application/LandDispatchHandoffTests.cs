using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandDispatchHandoffTests
{
    [Test]
    public async Task C1379_ReadyWaiterAcquiresBeforeNextLand()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true);
        await using var scope = world.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await using (var held = await world.Leases.TryAcquireAsync(world.Repo.Path, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            (await dispatcher.TickAsync(CancellationToken.None)).HeldOnLease.ShouldBe(1);
        }
        await dispatcher.DispatchReadyWaitersAsync(CancellationToken.None);
        await using var observer = world.CreateContext();
        var claimed = await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.Repair.Id);
        claimed.Status.ShouldBe(AgentTaskStatus.Dispatched,
            "c1379-acquisition-order: release must service a real ready waiter before the next land turn");
        Directory.Exists(claimed.WorktreePath).ShouldBeTrue();
        claimed.AgentSessionId.ShouldNotBeNull();
    }
}

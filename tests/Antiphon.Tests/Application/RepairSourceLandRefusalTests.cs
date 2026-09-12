using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RepairSourceLandRefusalTests
{
    [Test]
    [Timeout(90_000)]
    public async Task C499_V24ii_LandRequestIsRefusedForARepairTask()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync("Fixed it.\n" + world.ClaimLine(c) + "\n");
        await using var scope = world.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var land = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            land.RequestAsync(repair.Id, new LandAgentTaskRequest(ExpectedSourceSha: c), default));
        ex.Code.ShouldBe("repair_source_landing_owner_required");
        ex.Message.ShouldContain(DelegationReportFormatter.Short(world.Owner.Id));
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == repair.Id)).ShouldBe(0);
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V24iii_ProtocolEntryIsRefusedForARepairTask()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync("Fixed it.\n" + world.ClaimLine(c) + "\n");
        await using var scope = world.Services.CreateAsyncScope();
        var protocol = scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>();
        var leases = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>();
        await using var lease = await leases.TryAcquireAsync(world.Repo.Path, default);
        lease.ShouldNotBeNull();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTasks.SingleAsync(t => t.Id == repair.Id);
        var ex = await Should.ThrowAsync<ConflictException>(() => protocol.RunAsync(row, lease!, default));
        ex.Code.ShouldBe("repair_source_landing_owner_required");
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentTaskLandings.CountAsync(o => o.TaskId == repair.Id))
            .ShouldBe(0);
    }
}

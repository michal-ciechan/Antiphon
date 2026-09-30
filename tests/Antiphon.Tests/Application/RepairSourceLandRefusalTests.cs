using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
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
    [Arguments("request")]
    [Arguments("worker")]
    [Arguments("protocol")]
    [Timeout(180_000)]
    public async Task C603_RepairLandRefusalNamesReviewedOwnerRecovery(string entry)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        await using (var seed = world.CreateContext())
        {
            var owner = await seed.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            owner.Status = AgentTaskStatus.Failed;
            await seed.SaveChangesAsync();
        }
        var (repair, _) = await world.DispatchAsync();
        var sha = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync(world.DoneReport("Fixed it.", sha));
        await using var scope = world.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<AgentTaskLandQueue>();
        var eventsBefore = await db.AgentTaskEvents.CountAsync();
        var notificationsBefore = await db.AgentTaskLandNotifications.CountAsync();
        var targetBefore = (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master")).StdOut.Trim();
        ConflictException error;
        if (entry == "request")
            error = await Should.ThrowAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<AgentTaskLandService>().RequestAsync(
                    repair.Id, new LandAgentTaskRequest(ExpectedSourceSha: sha), default));
        else if (entry == "worker")
            error = await Should.ThrowAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<AgentTaskLandService>().RunRequestAsync(
                    repair.Id, null, null, default));
        else
        {
            var leases = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>();
            await using var lease = await leases.TryAcquireAsync(world.Repo.Path, default);
            lease.ShouldNotBeNull();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
            error = await Should.ThrowAsync<ConflictException>(() =>
                scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>().RunAsync(row, lease!, default));
        }
        error.StatusCode.ShouldBe(409);
        error.Code.ShouldBe("repair_source_landing_owner_required");
        error.Message.ShouldContain(world.Owner.Id.ToString("D"));
        error.Message.ShouldContain("-Land " + world.Owner.Id.ToString("D"));
        error.Message.ShouldContain("subjectTaskId");
        error.Message.ShouldContain("Clean Final/Full Review");
        error.Message.ShouldContain("-ExpectedSourceSha <full-sha>");
        error.Message.ShouldContain("-ReviewEvidenceId <review-evidence-id>");
        error.Message.ShouldContain("-RecoverReviewedSource");
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
        (await db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync()).ShouldBe(eventsBefore);
        (await db.AgentTaskLandNotifications.CountAsync()).ShouldBe(notificationsBefore);
        queue.PendingCount.ShouldBe(0);
        (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master"))
            .StdOut.Trim().ShouldBe(targetBefore);
    }

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

    [Test]
    [Timeout(90_000)]
    public async Task C499_V24iv_OwnerLandRequestStillQueues()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync("Fixed it.\n" + world.ClaimLine(c) + "\n");
        await using var scope = world.Services.CreateAsyncScope();
        var land = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        var queue = scope.ServiceProvider.GetRequiredService<AgentTaskLandQueue>();
        var result = await land.RequestAsync(world.Owner.Id, new LandAgentTaskRequest(ExpectedSourceSha: c), default);
        result.Status.ShouldBe("queued");
        queue.IsActive(repair.Id).ShouldBeFalse();
    }
}

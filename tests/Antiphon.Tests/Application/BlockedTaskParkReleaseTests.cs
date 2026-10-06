using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class BlockedTaskParkReleaseTests
{
    [Test]
    public async Task C1065_ReportPublicationPrecedesPhysicalRelease()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.ReleaseAsync();
        f.Wire.ConditionalCommands.ShouldBe(0, "G-95: direct CARD-0667 entry requires publication");
        await using var db = f.Db();
        (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
        (await db.RunnerSeatReleases.AnyAsync(r => r.ActionId != null)).ShouldBeFalse("G-95: no reservation without source proof");
    }

    [Test]
    public async Task C1065_EachBlockCauseUsesCurrentIdleProof()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.EditAsync((task, _) => { task.CompletedAt = null; task.Result = null; });
        using (var scope = f.Harness.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
                .TryHandleTaskAsync(f.TaskId, default);
        await using var db = f.Db();
        (await db.AgentTaskParks.CountAsync(p => p.TaskId == f.TaskId)).ShouldBe(1,
            "G-106: a nonreport block registers an episode without inventing a completion");
        (await f.TaskAsync()).CompletedAt.ShouldBeNull("G-106: no invented completion");
        (await f.TaskAsync()).Result.ShouldBeNull("G-106: no invented report");
        f.Wire.ConditionalCommands.ShouldBe(0, "G-108: unknown source ownership cannot release");
        f.Launches.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task C1065_ParkedAgentIsReservedButNotWarm()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await f.EditAsync((task, _) => task.Workspace = WorkspaceMode.Shared);
        await f.ReleaseFromSettlementAsync();
        await using var db = f.Db();
        var agent = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
        agent.PoolIdleSince.ShouldBeNull("G-110: blocked parking cannot put an owned identity in the warm pool");
        agent.PoolReservedForRootTaskId.ShouldBeNull("G-110: parked ownership is not a warm reservation");
        f.Wire.ForceCommands.ShouldBe(0);
        f.RecordedStops.Killed.ShouldBeEmpty();
    }
}

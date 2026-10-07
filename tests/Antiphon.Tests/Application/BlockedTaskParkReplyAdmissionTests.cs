using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1103 repair. Reply guidance follows the release identity answer admission checks.
/// Mark-read changes the settlement revision; that 422 must not recommend -Reply.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskParkReplyAdmissionTests
{
    [Test]
    public async Task C1103_RemotePoolReplyNamesOnlyAdmittedRelease()
    {
        await using (var admitted = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await BlockedTaskParkDeliveryTests.PublishAsync(admitted);
            await BlockedTaskParkDeliveryTests.StampAsync(admitted);
            var blockedShort = DelegationReportFormatter.Short(admitted.TaskId);
            var before = await CountAsync(admitted);
            var named = await Should.ThrowAsync<ValidationException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(admitted));
            named.StatusCode.ShouldBe(422, "G-8");
            named.Code.ShouldBe("follow_up_remote_pool_unsupported", "G-8");
            named.Message.ShouldContain($"-Reply {blockedShort}", Case.Sensitive, "G-8");
            named.Message.ShouldContain(blockedShort, Case.Sensitive, "G-8");
            named.Message.ShouldContain("without -OnAgent", Case.Sensitive, "G-8");
            named.Message.ShouldNotContain("cannot be continued", Case.Sensitive, "G-8");
            (await CountAsync(admitted)).ShouldBe(before, "G-8");

            await admitted.AnswerAsync("continue the parked answer");
            var resumed = await admitted.TaskAsync();
            resumed.Attempt.ShouldBe(2, "G-8");
            resumed.Status.ShouldBe(AgentTaskStatus.Queued, "G-8");
            resumed.ReleasedSeatAnswer.ShouldBe("continue the parked answer", "G-8");
            resumed.ReleasedSeatAnswerId.ShouldNotBeNull("G-8");
        }

        await using (var stale = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await BlockedTaskParkDeliveryTests.PublishAsync(stale);
            await BlockedTaskParkDeliveryTests.StampAsync(stale);
            using (var scope = stale.Harness.Provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                    .MarkReadAsync(stale.TaskId, CancellationToken.None);
            }

            var blockedShort = DelegationReportFormatter.Short(stale.TaskId);
            var before = await CountAsync(stale);
            var refused = await Should.ThrowAsync<ValidationException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(stale));
            refused.StatusCode.ShouldBe(422, "R1");
            refused.Code.ShouldBe("follow_up_remote_pool_unsupported", "R1");
            refused.Message.ShouldNotContain("-Reply", Case.Sensitive, "R1");
            refused.Message.ShouldContain(blockedShort, Case.Sensitive, "R1");
            refused.Message.ShouldContain("does not match answer admission", Case.Sensitive, "R1");
            refused.Message.ShouldContain("cannot be continued", Case.Sensitive, "R1");
            refused.Message.ShouldContain("without -OnAgent", Case.Sensitive, "R1");
            (await CountAsync(stale)).ShouldBe(before, "R1");
        }

        await using var previous = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await BlockedTaskParkDeliveryTests.PublishAsync(previous);
        await BlockedTaskParkDeliveryTests.StampAsync(previous);
        await using (var db = previous.Db())
        {
            var attempt = await db.AgentTasks.Where(t => t.Id == previous.TaskId).Select(t => t.Attempt).SingleAsync();
            var park = await db.AgentTaskParks.SingleAsync(p => p.TaskId == previous.TaskId && p.Attempt == attempt);
            park.Attempt = attempt + 1;
            await db.SaveChangesAsync();
        }

        var silent = await Should.ThrowAsync<ValidationException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(previous));
        silent.StatusCode.ShouldBe(422, "G-7");
        silent.Code.ShouldBe("follow_up_remote_pool_unsupported", "G-7");
        silent.Message.ShouldNotContain("-Reply", Case.Sensitive, "G-7");
        silent.Message.ShouldNotContain("cannot be continued", Case.Sensitive, "G-7");
        silent.Message.ShouldContain("without -OnAgent", Case.Sensitive, "G-7");
    }

    private static async Task<int> CountAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTasks.CountAsync();
    }
}

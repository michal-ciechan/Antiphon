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
/// A genuine settlement-revision change makes that 422 stop recommending -Reply. Mark-read
/// is not such a change (CARD-1144), and a stale confirmed park never falls back to the
/// old session.
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
            // A genuine task revision change; mark-read no longer is one (CARD-1144).
            await stale.EditAsync((task, _) => task.ConcurrencyToken = Guid.NewGuid());

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

    [Test]
    public async Task C1144_MarkReadPreservesConfirmedParkReply()
    {
        const string answer = "continue the parked answer: ünïcødé ✓ → 継続\nsecond line keeps its tail c1144-tail";
        foreach (var reads in new[] { 0, 1, 2 })
        {
            var label = $"c1144-reads-{reads}";
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await BlockedTaskParkDeliveryTests.PublishAsync(f);
            await BlockedTaskParkDeliveryTests.StampAsync(f);
            var parked = await f.TaskAsync();
            parked.ReadAt.ShouldBeNull(label);
            var oldSession = parked.AgentSessionId.ShouldNotBeNull(label);
            var release = await ReleaseAsync(f);
            release.SettlementRevision.ShouldBe(parked.ConcurrencyToken, label);

            DateTime? firstRead = null;
            for (var read = 0; read < reads; read++)
            {
                using var scope = f.Harness.Provider.CreateScope();
                var readAt = f.Now;
                var summary = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                    .MarkReadAsync(f.TaskId, CancellationToken.None);
                firstRead ??= readAt;
                summary.ReadAt.ShouldBe(firstRead, label);
                f.Clock.Advance(TimeSpan.FromMinutes(3));
            }

            var afterReads = await f.TaskAsync();
            afterReads.ReadAt.ShouldBe(firstRead, label);
            afterReads.ConcurrencyToken.ShouldBe(parked.ConcurrencyToken, label);
            afterReads.Attempt.ShouldBe(parked.Attempt, label);
            afterReads.Status.ShouldBe(AgentTaskStatus.Blocked, label);
            ShouldMatch(await ReleaseAsync(f), release, label);

            // The follow-up guidance still names this task's Reply and inserts nothing.
            var blockedShort = DelegationReportFormatter.Short(f.TaskId);
            var before = await CountAsync(f);
            var named = await Should.ThrowAsync<ValidationException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(f));
            named.StatusCode.ShouldBe(422, label);
            named.Code.ShouldBe("follow_up_remote_pool_unsupported", label);
            named.Message.ShouldContain($"-Reply {blockedShort}", Case.Sensitive, label);
            named.Message.ShouldNotContain("cannot be continued", Case.Sensitive, label);
            (await CountAsync(f)).ShouldBe(before, label);

            var queuedBefore = await QueuedAsync(f, oldSession);
            var launchesBefore = f.Launches.Calls.Count;
            await f.AnswerAsync(answer);

            var resumed = await f.TaskAsync();
            resumed.Attempt.ShouldBe(parked.Attempt + 1, label);
            resumed.Status.ShouldBe(AgentTaskStatus.Queued, label);
            resumed.ReleasedSeatAnswer.ShouldBe(answer, label);
            resumed.ReadAt.ShouldBe(firstRead, label);
            var answerId = resumed.ReleasedSeatAnswerId.ShouldNotBeNull(label);
            await using (var db = f.Db())
            {
                var replied = await db.AgentTaskEvents.AsNoTracking()
                    .Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Replied)
                    .Select(e => e.Id).ToListAsync();
                replied.Count.ShouldBe(1, label);
                replied[0].ShouldBe(answerId, label);
                (await db.SessionQueuedMessages.AsNoTracking()
                    .AnyAsync(m => m.ExecutionTaskId == f.TaskId && m.AgentSessionId == oldSession)).ShouldBeFalse(label);
            }
            (await QueuedAsync(f, oldSession)).ShouldBe(queuedBefore, label);
            f.Launches.Calls.Count.ShouldBe(launchesBefore, label);
            ShouldMatch(await ReleaseAsync(f), release, label);
        }
    }

    [Test]
    public async Task C1144_StaleParkNeverFallsBackToOldSession()
    {
        foreach (var world in new[] { "revision-changed", "session-deleted" })
        {
            var label = $"c1144-{world}";
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await BlockedTaskParkDeliveryTests.PublishAsync(f);
            await BlockedTaskParkDeliveryTests.StampAsync(f);
            var release = await ReleaseAsync(f);
            release.State.ShouldBe(RunnerSeatReleaseState.Confirmed, label);
            if (world == "revision-changed")
            {
                // Models a row already damaged by the pre-fix mark-read: the release is untouched.
                await f.EditAsync((task, _) => task.ConcurrencyToken = Guid.NewGuid());
            }
            else
            {
                await using var db = f.Db();
                var sessionId = (await f.TaskAsync()).AgentSessionId.ShouldNotBeNull(label);
                (await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync()).ShouldBe(1, label);
            }

            var stale = await f.TaskAsync();
            var oldSession = stale.AgentSessionId.ShouldNotBeNull(label);
            var queuedBefore = await QueuedAsync(f, oldSession);
            var repliedBefore = await RepliedAsync(f);
            var launchesBefore = f.Launches.Calls.Count;

            var refused = (await f.TryAnswerAsync("answer for a stale park")).ShouldBeOfType<ConflictException>(label);
            refused.StatusCode.ShouldBe(409, label);
            refused.Code.ShouldBe("park_release_identity_mismatch", label);

            var after = await f.TaskAsync();
            after.Status.ShouldBe(AgentTaskStatus.Blocked, label);
            after.Attempt.ShouldBe(stale.Attempt, label);
            after.AgentSessionId.ShouldBe(oldSession, label);
            after.ConcurrencyToken.ShouldBe(stale.ConcurrencyToken, label);
            after.ReleasedSeatAnswer.ShouldBeNull(label);
            after.ReleasedSeatAnswerId.ShouldBeNull(label);
            after.RepliedAt.ShouldBe(stale.RepliedAt, label);
            (await RepliedAsync(f)).ShouldBe(repliedBefore, label);
            (await QueuedAsync(f, oldSession)).ShouldBe(queuedBefore, label);
            f.Launches.Calls.Count.ShouldBe(launchesBefore, label);
            ShouldMatch(await ReleaseAsync(f), release, label);
        }
    }

    [Test]
    [Arguments("TaskId")]
    [Arguments("Attempt")]
    [Arguments("SessionId")]
    [Arguments("AgentId")]
    [Arguments("RunnerId")]
    [Arguments("RunnerStoreId")]
    [Arguments("AcceptedStartedAt")]
    [Arguments("SettlementRevision")]
    [Arguments("SettledAt")]
    public async Task C1146_ReleaseIdentityParity(string field)
    {
        var label = $"c1146-release-{field}";
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await BlockedTaskParkDeliveryTests.PublishAsync(f);
        await BlockedTaskParkDeliveryTests.StampAsync(f);
        var original = await ReleaseAsync(f);
        await ShouldAdmitAsync(f, original.Id, $"{label}-before");

        // Historical release fields have no foreign keys: change the ledger only.
        await EditReleaseAsync(f, r =>
        {
            switch (field)
            {
                case "TaskId": r.TaskId = Guid.NewGuid(); break;
                case "Attempt": r.Attempt = r.Attempt + 1; break;
                case "SessionId": r.SessionId = Guid.NewGuid(); break;
                case "AgentId": r.AgentId = Guid.NewGuid(); break;
                case "RunnerId": r.RunnerId = "other-runner"; break;
                case "RunnerStoreId": r.RunnerStoreId = Guid.NewGuid(); break;
                case "AcceptedStartedAt": r.AcceptedStartedAt = r.AcceptedStartedAt.AddSeconds(1); break;
                case "SettlementRevision": r.SettlementRevision = Guid.NewGuid(); break;
                case "SettledAt": r.SettledAt = r.SettledAt.ShouldNotBeNull(label).AddSeconds(1); break;
                default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
            }
        });
        await ShouldNotAdmitAsync(f, label);
        (await GuidanceAsync(f)).ShouldBe(AgentTaskService.RemotePoolParkReply.ReleaseMismatch, label);
        var refused = await RemoteFollowUpRefusedAsync(f, label);
        refused.Message.ShouldNotContain("-Reply", Case.Sensitive, label);
        refused.Message.ShouldContain("does not match answer admission", Case.Sensitive, label);
        await ShouldVetoAnswerAsync(f, label);

        // Restoring that one field restores admission and the confirmed continuation.
        await EditReleaseAsync(f, r => RestoreIdentity(r, original));
        await ShouldAdmitAsync(f, original.Id, $"{label}-restored");
        await ShouldContinueAsync(f, $"{label}-restored");
    }

    [Test]
    [Arguments("missing-session")]
    [Arguments("session-runner-mismatch")]
    [Arguments("task-runner-null")]
    [Arguments("task-runner-empty")]
    [Arguments("task-session-null")]
    [Arguments("session-store-null")]
    public async Task C1146_SessionIdentityParity(string world)
    {
        var label = $"c1146-session-{world}";
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await BlockedTaskParkDeliveryTests.PublishAsync(f);
        await BlockedTaskParkDeliveryTests.StampAsync(f);
        var release = await ReleaseAsync(f);
        await ShouldAdmitAsync(f, release.Id, $"{label}-before");

        await using (var db = f.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
            var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
            switch (world)
            {
                // The task keeps its session ID and the ledger stays confirmed.
                case "missing-session":
                    (await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync()).ShouldBe(1, label);
                    break;
                case "session-runner-mismatch": session.RunnerId = "other-runner"; break;
                case "task-runner-null": task.RunnerId = null; break;
                case "task-runner-empty": task.RunnerId = ""; break;
                case "task-session-null": task.AgentSessionId = null; break;
                case "session-store-null": session.RunnerStoreId = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(world), world, null);
            }
            await db.SaveChangesAsync();
        }

        await ShouldNotAdmitAsync(f, label);
        // The guidance query itself, independent of how Create routes this follow-up.
        (await GuidanceAsync(f)).ShouldBe(AgentTaskService.RemotePoolParkReply.ReleaseMismatch, label);
        if (world is "task-runner-null" or "task-runner-empty")
        {
            // No retained remote runner: Create keeps its existing local 409 precedence.
            var before = await CountAsync(f);
            var local = await Should.ThrowAsync<ConflictException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(f));
            local.StatusCode.ShouldBe(409, label);
            local.Code.ShouldBe("follow_up_agent_blocked", label);
            (await CountAsync(f)).ShouldBe(before, label);
        }
        else
        {
            var refused = await RemoteFollowUpRefusedAsync(f, label);
            refused.Message.ShouldNotContain("-Reply", Case.Sensitive, label);
            refused.Message.ShouldContain("does not match answer admission", Case.Sensitive, label);
        }
        await ShouldVetoAnswerAsync(f, label);
        ShouldMatch(await ReleaseAsync(f), release, label);
    }

    private static async Task<AgentTaskService.RemotePoolParkReply> GuidanceAsync(RunnerSeatReleaseFixture f)
    {
        var task = await f.TaskAsync();
        using var scope = f.Harness.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .RemotePoolParkReplyAsync(task, CancellationToken.None);
    }

    private static async Task<(RunnerSeatRelease? Exact, List<RunnerSeatRelease> Identity, List<RunnerSeatRelease> Confirmed)>
        QueriesAsync(RunnerSeatReleaseFixture f)
    {
        var task = await f.TaskAsync();
        await using var db = f.Db();
        var exact = await TerminalRunnerSeatReleaseService.FindAttemptReleaseAsync(db, task, CancellationToken.None);
        var identity = await RunnerSeatReleaseQueries.ForAttempt(db, task).ToListAsync();
        var confirmed = await RunnerSeatReleaseQueries.Confirmed(RunnerSeatReleaseQueries.ForAttempt(db, task)).ToListAsync();
        return (exact, identity, confirmed);
    }

    private static async Task ShouldAdmitAsync(RunnerSeatReleaseFixture f, Guid releaseId, string label)
    {
        var (exact, identity, confirmed) = await QueriesAsync(f);
        exact.ShouldNotBeNull(label).Id.ShouldBe(releaseId, label);
        TerminalRunnerSeatReleaseService.IsConfirmed(exact).ShouldBeTrue(label);
        identity.Select(r => r.Id).ShouldBe([releaseId], label);
        confirmed.Select(r => r.Id).ShouldBe([releaseId], label);
        (await GuidanceAsync(f)).ShouldBe(AgentTaskService.RemotePoolParkReply.Admitted, label);
        var named = await RemoteFollowUpRefusedAsync(f, label);
        named.Message.ShouldContain($"-Reply {DelegationReportFormatter.Short(f.TaskId)}", Case.Sensitive, label);
        named.Message.ShouldNotContain("cannot be continued", Case.Sensitive, label);
    }

    private static async Task ShouldNotAdmitAsync(RunnerSeatReleaseFixture f, string label)
    {
        // The direct exact-identity read first, before any transactional guard could mask it.
        var (exact, identity, confirmed) = await QueriesAsync(f);
        exact.ShouldBeNull(label);
        identity.ShouldBeEmpty(label);
        confirmed.ShouldBeEmpty(label);
    }

    private static async Task<ValidationException> RemoteFollowUpRefusedAsync(RunnerSeatReleaseFixture f, string label)
    {
        var before = await CountAsync(f);
        var refused = await Should.ThrowAsync<ValidationException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(f));
        refused.StatusCode.ShouldBe(422, label);
        refused.Code.ShouldBe("follow_up_remote_pool_unsupported", label);
        refused.Message.ShouldContain("without -OnAgent", Case.Sensitive, label);
        (await CountAsync(f)).ShouldBe(before, label);
        return refused;
    }

    // The confirmed park's answer neither continues nor falls back to the old session.
    private static async Task ShouldVetoAnswerAsync(RunnerSeatReleaseFixture f, string label)
    {
        var stale = await f.TaskAsync();
        var queuedBefore = await QueuedAsync(f, f.SessionId);
        var repliedBefore = await RepliedAsync(f);
        var launchesBefore = f.Launches.Calls.Count;

        var refused = (await f.TryAnswerAsync($"answer for {label}")).ShouldBeOfType<ConflictException>(label);
        refused.StatusCode.ShouldBe(409, label);
        refused.Code.ShouldBe("park_release_identity_mismatch", label);

        var after = await f.TaskAsync();
        after.Status.ShouldBe(AgentTaskStatus.Blocked, label);
        after.Attempt.ShouldBe(stale.Attempt, label);
        after.AgentSessionId.ShouldBe(stale.AgentSessionId, label);
        after.ConcurrencyToken.ShouldBe(stale.ConcurrencyToken, label);
        after.ReleasedSeatAnswer.ShouldBeNull(label);
        after.ReleasedSeatAnswerId.ShouldBeNull(label);
        after.RepliedAt.ShouldBe(stale.RepliedAt, label);
        (await RepliedAsync(f)).ShouldBe(repliedBefore, label);
        (await QueuedAsync(f, f.SessionId)).ShouldBe(queuedBefore, label);
        f.Launches.Calls.Count.ShouldBe(launchesBefore, label);
    }

    // A confirmed continuation: one accepted answer, a new queued attempt, nothing typed to the old seat.
    private static async Task ShouldContinueAsync(RunnerSeatReleaseFixture f, string label)
    {
        var parked = await f.TaskAsync();
        var queuedBefore = await QueuedAsync(f, f.SessionId);
        var launchesBefore = f.Launches.Calls.Count;
        var answer = $"continue {label}: ünïcødé ✓";
        await f.AnswerAsync(answer);

        var resumed = await f.TaskAsync();
        resumed.Attempt.ShouldBe(parked.Attempt + 1, label);
        resumed.Status.ShouldBe(AgentTaskStatus.Queued, label);
        resumed.ReleasedSeatAnswer.ShouldBe(answer, label);
        resumed.ReleasedSeatAnswerId.ShouldNotBeNull(label);
        (await RepliedAsync(f)).ShouldBe(1, label);
        (await QueuedAsync(f, f.SessionId)).ShouldBe(queuedBefore, label);
        f.Launches.Calls.Count.ShouldBe(launchesBefore, label);
    }

    private static async Task EditReleaseAsync(RunnerSeatReleaseFixture f, Action<RunnerSeatRelease> edit)
    {
        await using var db = f.Db();
        // Keyed by the park's link, so a corrupted TaskId is still found and restored.
        var releaseId = await db.AgentTaskParks.Where(p => p.TaskId == f.TaskId)
            .Select(p => p.RunnerSeatReleaseId).SingleAsync();
        var release = await db.RunnerSeatReleases.SingleAsync(r => r.Id == releaseId);
        edit(release);
        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    private static void RestoreIdentity(RunnerSeatRelease row, RunnerSeatRelease original)
    {
        row.TaskId = original.TaskId;
        row.Attempt = original.Attempt;
        row.SessionId = original.SessionId;
        row.AgentId = original.AgentId;
        row.RunnerId = original.RunnerId;
        row.RunnerStoreId = original.RunnerStoreId;
        row.AcceptedStartedAt = original.AcceptedStartedAt;
        row.SettlementRevision = original.SettlementRevision;
        row.SettledAt = original.SettledAt;
    }

    private static async Task<RunnerSeatRelease> ReleaseAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.TaskId == f.TaskId);
    }

    private static void ShouldMatch(RunnerSeatRelease actual, RunnerSeatRelease expected, string label)
    {
        actual.Id.ShouldBe(expected.Id, label);
        actual.TaskId.ShouldBe(expected.TaskId, label);
        actual.Attempt.ShouldBe(expected.Attempt, label);
        actual.SessionId.ShouldBe(expected.SessionId, label);
        actual.AgentId.ShouldBe(expected.AgentId, label);
        actual.RunnerId.ShouldBe(expected.RunnerId, label);
        actual.RunnerStoreId.ShouldBe(expected.RunnerStoreId, label);
        actual.AcceptedStartedAt.ShouldBe(expected.AcceptedStartedAt, label);
        actual.SettlementRevision.ShouldBe(expected.SettlementRevision, label);
        actual.SettledAt.ShouldBe(expected.SettledAt, label);
        actual.State.ShouldBe(expected.State, label);
        actual.ConfirmedAt.ShouldBe(expected.ConfirmedAt, label);
    }

    private static async Task<int> QueuedAsync(RunnerSeatReleaseFixture f, Guid sessionId)
    {
        await using var db = f.Db();
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId);
    }

    private static async Task<int> RepliedAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Replied);
    }

    private static async Task<int> CountAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTasks.CountAsync();
    }
}

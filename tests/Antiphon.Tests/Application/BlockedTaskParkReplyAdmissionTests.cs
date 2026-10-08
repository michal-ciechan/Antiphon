using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
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
/// old session. The local 409 uses the same classification (CARD-1154).
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
    [Arguments("session-store-mismatch")]
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
                // CK_AgentSessions_RunnerBinding_AllOrNone forbids a lone null store, so the
                // session side of the store equality is exercised with a different store.
                case "session-store-mismatch": session.RunnerStoreId = Guid.NewGuid(); break;
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

    [Test]
    [Arguments("Released", true)]
    [Arguments("AlreadyExited", true)]
    [Arguments("AlreadyAbsent", true)]
    [Arguments("Observing", false)]
    [Arguments("Reserved", false)]
    [Arguments("Unresolved", false)]
    [Arguments("null-ConfirmedAt", false)]
    [Arguments("null-ActionId", false)]
    [Arguments("null-OutcomeCode", false)]
    [Arguments("unknown-OutcomeCode", false)]
    public async Task C1146_ConfirmationParity(string receipt, bool confirmed)
    {
        var label = $"c1146-confirmation-{receipt}";
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await BlockedTaskParkDeliveryTests.PublishAsync(f);
        await BlockedTaskParkDeliveryTests.StampAsync(f);
        var release = await ReleaseAsync(f);
        release.State.ShouldBe(RunnerSeatReleaseState.Confirmed, label);

        // Identity stays exact; only the confirmation receipt changes.
        await EditReleaseAsync(f, r =>
        {
            switch (receipt)
            {
                case "Released" or "AlreadyExited" or "AlreadyAbsent": r.OutcomeCode = receipt; break;
                case "Observing": r.State = RunnerSeatReleaseState.Observing; break;
                case "Reserved": r.State = RunnerSeatReleaseState.Reserved; break;
                case "Unresolved": r.State = RunnerSeatReleaseState.Unresolved; break;
                case "null-ConfirmedAt": r.ConfirmedAt = null; break;
                case "null-ActionId": r.ActionId = null; break;
                case "null-OutcomeCode": r.OutcomeCode = null; break;
                case "unknown-OutcomeCode": r.OutcomeCode = nameof(TerminalSeatReleaseOutcome.Unknown); break;
                default: throw new ArgumentOutOfRangeException(nameof(receipt), receipt, null);
            }
        });

        var (exact, identity, confirmedRows) = await QueriesAsync(f);
        exact.ShouldNotBeNull(label).Id.ShouldBe(release.Id, label);
        identity.Select(r => r.Id).ShouldBe(new[] { release.Id }, label);
        // The shared confirmed query and IsConfirmed agree on every receipt.
        TerminalRunnerSeatReleaseService.IsConfirmed(exact).ShouldBe(confirmed, label);
        confirmedRows.Select(r => r.Id).ShouldBe(confirmed ? new[] { release.Id } : Array.Empty<Guid>(), label);
        await using (var db = f.Db())
        {
            var continuation = await TerminalRunnerSeatReleaseService.FindConfirmedAttemptReleaseAsync(
                db, await f.TaskAsync(), CancellationToken.None);
            (continuation?.Id).ShouldBe(confirmed ? release.Id : null, label);
        }

        var guidance = await GuidanceAsync(f);
        var refused = await RemoteFollowUpRefusedAsync(f, label);
        if (confirmed)
        {
            guidance.ShouldBe(AgentTaskService.RemotePoolParkReply.Admitted, label);
            refused.Message.ShouldContain($"-Reply {DelegationReportFormatter.Short(f.TaskId)}", Case.Sensitive, label);
            await ShouldContinueAsync(f, label);
        }
        else
        {
            // Reserved/Unresolved keep their existing buffering contract; nothing here
            // requires an immediate attempt, and guidance never offers Reply.
            guidance.ShouldNotBe(AgentTaskService.RemotePoolParkReply.Admitted, label);
            refused.Message.ShouldNotContain("-Reply", Case.Sensitive, label);
        }
    }

    [Test]
    [Arguments("Parked", true)]
    [Arguments("ResumePending", true)]
    [Arguments("foreign-task", false)]
    [Arguments("prior-attempt", false)]
    [Arguments("null-publication-receipt", false)]
    [Arguments("null-linked-release", false)]
    [Arguments("Resumed", false)]
    public async Task C1146_ParkScopeParity(string scope, bool named)
    {
        var label = $"c1146-park-{scope}";
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await BlockedTaskParkDeliveryTests.PublishAsync(f);
        await BlockedTaskParkDeliveryTests.StampAsync(f);
        var release = await ReleaseAsync(f);
        await ShouldAdmitAsync(f, release.Id, $"{label}-before");

        // Only the park changes; the release still matches exactly.
        await using (var db = f.Db())
        {
            var park = await db.AgentTaskParks.SingleAsync(p => p.TaskId == f.TaskId);
            park.State.ShouldBe(AgentTaskParkState.Parked, label);
            switch (scope)
            {
                case "Parked": break;
                case "ResumePending": park.State = AgentTaskParkState.ResumePending; break;
                case "foreign-task": park.TaskId = Guid.NewGuid(); break;
                case "prior-attempt": park.Attempt = park.Attempt - 1; break;
                case "null-publication-receipt": park.PublicationReceiptId = null; break;
                case "null-linked-release": park.RunnerSeatReleaseId = null; break;
                case "Resumed": park.State = AgentTaskParkState.Resumed; break;
                default: throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
            }
            await db.SaveChangesAsync();
        }
        var (exact, _, confirmedRows) = await QueriesAsync(f);
        exact.ShouldNotBeNull(label).Id.ShouldBe(release.Id, label);
        confirmedRows.Select(r => r.Id).ShouldBe(new[] { release.Id }, label);

        (await GuidanceAsync(f)).ShouldBe(
            named ? AgentTaskService.RemotePoolParkReply.Admitted : AgentTaskService.RemotePoolParkReply.None, label);
        var refused = await RemoteFollowUpRefusedAsync(f, label);
        if (named)
        {
            refused.Message.ShouldContain($"-Reply {DelegationReportFormatter.Short(f.TaskId)}", Case.Sensitive, label);
            await ShouldContinueAsync(f, label);
        }
        else
        {
            refused.Message.ShouldNotContain("-Reply", Case.Sensitive, label);
            refused.Message.ShouldNotContain("cannot be continued", Case.Sensitive, label);
        }
    }

    [Test]
    public async Task C1146_GuidanceUsesOneStatement()
    {
        foreach (var world in new[] { "admitted", "stale", "missing-session", "multiple-parks" })
        {
            var label = $"c1146-sql-{world}";
            var counter = new FullCommandCounter();
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
                configureDb: options => options.AddInterceptors(counter));
            await BlockedTaskParkDeliveryTests.PublishAsync(f);
            await BlockedTaskParkDeliveryTests.StampAsync(f);
            var release = await ReleaseAsync(f);
            var expected = AgentTaskService.RemotePoolParkReply.Admitted;
            switch (world)
            {
                case "admitted": break;
                case "stale":
                    await f.EditAsync((task, _) => task.ConcurrencyToken = Guid.NewGuid());
                    expected = AgentTaskService.RemotePoolParkReply.ReleaseMismatch;
                    break;
                case "missing-session":
                {
                    await using var db = f.Db();
                    (await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync()).ShouldBe(1, label);
                    expected = AgentTaskService.RemotePoolParkReply.ReleaseMismatch;
                    break;
                }
                case "multiple-parks":
                {
                    // Two more current-attempt parks; a per-park read would add statements.
                    await using var db = f.Db();
                    var park = await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.TaskId == f.TaskId);
                    foreach (var _ in new[] { 1, 2 })
                    {
                        db.AgentTaskParks.Add(new AgentTaskPark
                        {
                            Id = Guid.NewGuid(), TaskId = park.TaskId, Attempt = park.Attempt,
                            BlockEventId = Guid.NewGuid(), TaskConcurrencyToken = park.TaskConcurrencyToken,
                            PublicationReceiptId = Guid.NewGuid(), RunnerSeatReleaseId = release.Id,
                            State = AgentTaskParkState.Parked, ReasonCode = "park_parked",
                            BlockedAt = park.BlockedAt, CreatedAt = f.Now, UpdatedAt = f.Now,
                        });
                    }
                    await db.SaveChangesAsync();
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(world), world);
            }

            // Measure the guidance query alone; seeding and reads above use an uncounted context.
            var task = await f.TaskAsync();
            AgentTaskService.RemotePoolParkReply actual;
            using (var scope = f.Harness.Provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
                counter.Reset();
                actual = await service.RemotePoolParkReplyAsync(task, CancellationToken.None);
            }
            var roster = counter.Roster();
            Console.WriteLine($"C1146-SQL world={world} statements={counter.Total}\n{roster}");
            actual.ShouldBe(expected, label);
            counter.Total.ShouldBe(1, $"{label}\n{roster}");
            var sql = counter.Commands[0].TrimStart();
            sql.ShouldStartWith("SELECT", Case.Insensitive, label);
            sql.ShouldContain("\"AgentTaskParks\"", Case.Sensitive, label);
            sql.ShouldContain("\"RunnerSeatReleases\"", Case.Sensitive, label);
            sql.ShouldContain("\"AgentSessions\"", Case.Sensitive, label);
            foreach (var write in new[] { "INSERT ", "UPDATE ", "DELETE " })
                sql.ShouldNotContain(write, Case.Insensitive, label);

            // Public Create gives the same classification and inserts nothing.
            var refused = await RemoteFollowUpRefusedAsync(f, label);
            if (expected == AgentTaskService.RemotePoolParkReply.Admitted)
                refused.Message.ShouldContain($"-Reply {DelegationReportFormatter.Short(f.TaskId)}", Case.Sensitive, label);
            else
            {
                refused.Message.ShouldNotContain("-Reply", Case.Sensitive, label);
                refused.Message.ShouldContain("does not match answer admission", Case.Sensitive, label);
            }
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
    public async Task C1154_LocalReleaseIdentityParity(string field)
    {
        var label = $"c1154-local-release-{field}";
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await BlockedTaskParkDeliveryTests.PublishAsync(f);
        await BlockedTaskParkDeliveryTests.StampAsync(f);
        // The local branch with admissible identity: only the pool flag changes, so task,
        // session, store and release bindings all stay exact.
        await f.EditAsync((_, agent) => agent.IsPoolDelegate = false);
        var original = await ReleaseAsync(f);
        await ShouldAdmitLocallyAsync(f, original.Id, $"{label}-before");

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
        var refused = await LocalFollowUpRefusedAsync(f, label);
        refused.Message.ShouldNotContain("-Reply", Case.Sensitive, label);
        refused.Message.ShouldNotContain("cancel", Case.Insensitive, label);
        refused.Message.ShouldNotContain("published seat was released", Case.Sensitive, label);
        refused.Message.ShouldContain("does not match answer admission", Case.Sensitive, label);
        refused.Message.ShouldContain("cannot be continued", Case.Sensitive, label);
        await ShouldVetoAnswerAsync(f, label);

        // Restoring that one field restores the local Reply advice and the confirmed continuation.
        await EditReleaseAsync(f, r => RestoreIdentity(r, original));
        await ShouldAdmitLocallyAsync(f, original.Id, $"{label}-restored");
        await ShouldContinueAsync(f, $"{label}-restored");
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
        identity.Select(r => r.Id).ShouldBe(new[] { releaseId }, label);
        confirmed.Select(r => r.Id).ShouldBe(new[] { releaseId }, label);
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

    private static async Task ShouldAdmitLocallyAsync(RunnerSeatReleaseFixture f, Guid releaseId, string label)
    {
        var (exact, identity, confirmed) = await QueriesAsync(f);
        exact.ShouldNotBeNull(label).Id.ShouldBe(releaseId, label);
        TerminalRunnerSeatReleaseService.IsConfirmed(exact).ShouldBeTrue(label);
        identity.Select(r => r.Id).ShouldBe(new[] { releaseId }, label);
        confirmed.Select(r => r.Id).ShouldBe(new[] { releaseId }, label);
        (await GuidanceAsync(f)).ShouldBe(AgentTaskService.RemotePoolParkReply.Admitted, label);
        var named = await LocalFollowUpRefusedAsync(f, label);
        named.Message.ShouldContain("The published seat was released.", Case.Sensitive, label);
        named.Message.ShouldContain($"-Reply {DelegationReportFormatter.Short(f.TaskId)}", Case.Sensitive, label);
        named.Message.ShouldNotContain("does not match answer admission", Case.Sensitive, label);
        named.Message.ShouldNotContain("/cancel", Case.Sensitive, label);
    }

    // The local 409 changes nothing: no task, answer, event, old-session input, launch or stop.
    private static async Task<ConflictException> LocalFollowUpRefusedAsync(RunnerSeatReleaseFixture f, string label)
    {
        var before = await f.TaskAsync();
        var count = await CountAsync(f);
        var replied = await RepliedAsync(f);
        var queued = await QueuedAsync(f, f.SessionId);
        var launches = f.Launches.Calls.Count;
        var kills = f.RecordedStops.Killed.Count;

        var refused = await Should.ThrowAsync<ConflictException>(() => BlockedTaskParkDeliveryTests.FollowUpAsync(f));
        refused.StatusCode.ShouldBe(409, label);
        refused.Code.ShouldBe("follow_up_agent_blocked", label);
        refused.Message.ShouldContain(
            $"parked on Blocked task {DelegationReportFormatter.Short(f.TaskId)}", Case.Sensitive, label);
        refused.Message.ShouldNotContain("without -OnAgent", Case.Sensitive, label);

        var after = await f.TaskAsync();
        after.Status.ShouldBe(before.Status, label);
        after.Attempt.ShouldBe(before.Attempt, label);
        after.ConcurrencyToken.ShouldBe(before.ConcurrencyToken, label);
        after.ReleasedSeatAnswerId.ShouldBe(before.ReleasedSeatAnswerId, label);
        (await CountAsync(f)).ShouldBe(count, label);
        (await RepliedAsync(f)).ShouldBe(replied, label);
        (await QueuedAsync(f, f.SessionId)).ShouldBe(queued, label);
        f.Launches.Calls.Count.ShouldBe(launches, label);
        f.RecordedStops.Killed.Count.ShouldBe(kills, label);
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
        await db.SaveChangesAsync();
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

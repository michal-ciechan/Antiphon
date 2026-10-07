using System.Data.Common;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1065 S8. Publication-gated park delivery, not the historical release seed.
/// Each method is one TUnit result; labeled scenarios are not extra checkpoint rows.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskParkDeliveryTests
{
    private const string Canary = "FINAL-ANSWER-CANARY";
    private const string ContinueAuthority = "proceed on the published checkout";

    [Test]
    public async Task C1065_FullAnswerRequiresNewSessionUserPrompt()
    {
        var ceiling = new DelegationSettings().BriefInlineMaxBytes;
        foreach (var answer in new[]
                 {
                     CharAnswer(3999), CharAnswer(4000), CharAnswer(4001),
                     ByteAnswer(ceiling - 1), ByteAnswer(ceiling), ByteAnswer(ceiling + 1),
                 })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            await DeliverAsync(f, answer, busy: true);
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f);
            await StampAsync(f);
            await f.AnswerAsync("receipt canary");
            f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
            await f.DispatchAsync();
            var row = (await f.AnswerQueueAsync())!;
            var held = await f.TaskAsync();
            await using var db = f.Db();
            var generation = (await db.AgentSessions.SingleAsync(s => s.Id == row.AgentSessionId)).StartedAt;
            foreach (var shape in new[] { "ack", "partial", "wrong", "stale", "wrong-session", "queued", "assistant", "generation" })
            {
                var text = shape switch
                {
                    "wrong" => "different prompt",
                    "partial" => row.Body[..Math.Min(100, row.Body.Length)],
                    _ => row.Body,
                };
                // A prior shape's complete destination prompt stays in the transcript. The next
                // shape's floor keeps that prompt below the receipt window.
                var floor = await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId)
                    .MaxAsync(t => (long?)t.Sequence) ?? 0;
                if (shape != "ack")
                {
                    await f.NativePromptAsync(shape == "wrong-session" ? f.SessionId : row.AgentSessionId, text);
                    if (shape is "queued" or "assistant")
                    {
                        await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId && t.Text == text
                                && t.Sequence > floor)
                            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind,
                                shape == "queued" ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.AssistantText));
                    }
                }

                var max = await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId).MaxAsync(t => (long?)t.Sequence) ?? 0;
                await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                    .SetProperty(m => m.DeliveryAttempts, 1)
                    .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.Delivered)
                    .SetProperty(m => m.LastDeliveryStartedAt, f.Now)
                    .SetProperty(m => m.LastDeliveryGeneration, shape == "generation" ? generation.AddSeconds(-1) : generation)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, shape == "stale" ? max : floor));
                await f.DispatchAsync();
                var pending = await f.TaskAsync();
                pending.ReleasedSeatAnswerId.ShouldBe(held.ReleasedSeatAnswerId, Label(shape));
                pending.ReleasedSeatAnswer.ShouldBe("receipt canary", Label(shape));
                await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.Status, QueuedMessageStatus.Pending)
                    .SetProperty(m => m.DeliveryAttempts, 0)
                    .SetProperty(m => m.DeliveryVerdict, (DeliveryVerdict?)null)
                    .SetProperty(m => m.LastDeliveryStartedAt, (DateTime?)null)
                    .SetProperty(m => m.LastDeliveryGeneration, (DateTime?)null)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null));
            }

            // The wrong-session probe is the test's own old-session prompt. Remove it so the
            // following receipt check counts only a prompt the product delivered.
            await db.TranscriptEntries.Where(t => t.AgentSessionId == f.SessionId && t.Text == row.Body)
                .ExecuteDeleteAsync();
            await f.EndTurnAsync(row.AgentSessionId);
            await f.FlushAsync(row.AgentSessionId);
            await f.DispatchAsync();
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull("G-155");
            await using var check = f.Db();
            (await check.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == row.Body)).ShouldBe(0, "G-156");
        }

        foreach (var busy in new[] { true, false })
        foreach (var boundary in new[]
                 {
                     "accept-before", "attempt-before", "queue-before", "queue-after",
                     "delivery-stamp", "verdict-before", "complete-before",
                 })
        {
            var cut = new DeliverySaveCut { Boundary = boundary };
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
                configureDb: options => options.AddInterceptors(cut, new DeliveryCommitCut(cut)));
            await PublishAsync(f);
            await StampAsync(f);
            cut.Armed = true;
            await f.TryAnswerAsync("recover exactly once");
            if (boundary == "accept-before")
            {
                (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBeNull(boundary);
                f.Launches.Calls.ShouldBeEmpty(boundary);
                await f.AnswerAsync("recover exactly once");
            }

            var answerId = (await f.TaskAsync()).ReleasedSeatAnswerId;
            answerId.ShouldNotBeNull(boundary);
            await f.RestartAsync();
            f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy).GetAwaiter().GetResult();
            try { await f.DispatchAsync(); }
            catch (InvalidOperationException) when (cut.Hit) { }
            var target = await f.TaskAsync();
            target.Attempt.ShouldBe(2, "G-161 " + boundary);
            var sessionId = target.AgentSessionId.ShouldNotBeNull(boundary);
            if (busy) f.Recipient?.Inputs.ShouldBeEmpty("G-162");
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBe(answerId, boundary);
            await f.RestartAsync();
            await f.AttachRecipientAsync(sessionId);
            try { await f.DispatchAsync(); }
            catch (InvalidOperationException) when (cut.Hit) { }
            var queued = await f.AnswerQueueAsync();
            queued.ShouldNotBeNull(boundary);
            await f.EndTurnAsync(sessionId);
            try { await f.FlushAsync(sessionId); }
            catch (InvalidOperationException) when (cut.Hit) { }
            await f.RestartAsync();
            await f.AttachRecipientAsync(sessionId);
            if (boundary is "delivery-stamp" or "verdict-before")
            {
                var interrupted = await f.AnswerQueueAsync();
                if (interrupted is { Status: QueuedMessageStatus.Sent, DeliveryVerdict: null })
                {
                    var settings = f.Harness.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification;
                    f.Clock.Advance(TimeSpan.FromSeconds(settings.TranscriptConfirmTimeoutSeconds
                        + settings.PostFailureConfirmGraceSeconds
                        + settings.UnobservableBaselineConfirmClockToleranceSeconds).Add(TimeSpan.FromTicks(1)));
                }
            }

            try { await f.FlushAsync(sessionId); }
            catch (InvalidOperationException) when (cut.Hit) { }
            await f.DispatchAsync();
            if (boundary == "complete-before") await f.DispatchAsync();
            if (boundary is not "accept-before") cut.Hit.ShouldBeTrue("G-161 " + boundary);
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull("G-161 " + boundary);
            await using var db = f.Db();
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == queued!.Body)).ShouldBe(1, "G-161 " + boundary);
            (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == f.TaskId)).ShouldBe(1, "G-161 " + boundary);
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId
                && m.ExecutionTaskId == f.TaskId)).ShouldBe(0, "G-156 " + boundary);
        }
    }

    [Test]
    public async Task C1065_ContinueAndPrerequisiteReplyUseGuardedPath()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await f.EditAsync((task, _) => task.Status = AgentTaskStatus.Queued);
            var refused = await Should.ThrowAsync<ConflictException>(() => ContinueAsync(f));
            refused.Code.ShouldBe("not_blocked", "G-163");
            f.Launches.Calls.ShouldBeEmpty("G-163");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await f.EditAsync((task, _) =>
            {
                task.FailureReason = BlockedQuestion.CostCeilingPrefix + " reached";
                task.StandingAuthority = ContinueAuthority;
            });
            var refused = await Should.ThrowAsync<ConflictException>(() => ContinueAsync(f));
            refused.Code.ShouldBe("not_a_question", "G-164");
            (await f.TaskAsync()).Attempt.ShouldBe(1, "G-164");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            var refused = await Should.ThrowAsync<ConflictException>(() => ContinueAsync(f));
            refused.Code.ShouldBe("no_authority", "G-165");
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBeNull("G-165");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f);
            await StampAsync(f);
            await f.EditAsync((task, _) => task.Result = "The prerequisite landed and the card is Done. Continue automatically.");
            await using (var db = f.Db())
            {
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(), AgentTaskId = f.TaskId, Type = AgentTaskEventType.Landed,
                    At = f.Now, Detail = "card moved to Done",
                });
                await db.SaveChangesAsync();
            }

            await f.DispatchAsync();
            f.Launches.Calls.ShouldBeEmpty("G-166");
            var untouched = await f.TaskAsync();
            untouched.Attempt.ShouldBe(1, "G-166");
            untouched.Status.ShouldBe(AgentTaskStatus.Blocked, "G-166");
            untouched.ReleasedSeatAnswerId.ShouldBeNull("G-166");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f);
            await StampAsync(f);
            await f.EditAsync((task, _) => task.StandingAuthority = ContinueAuthority);
            await ContinueAsync(f);
            var queued = await f.TaskAsync();
            queued.Attempt.ShouldBe(2, "G-163");
            queued.AgentId.ShouldBe(f.AgentId);
            queued.ReleasedSeatAnswer.ShouldContain(ContinueAuthority, Case.Sensitive);
            f.Launches.Calls.ShouldBeEmpty();
            await f.DispatchAsync();
            var launched = await f.TaskAsync();
            launched.Status.ShouldBe(AgentTaskStatus.Dispatched, "G-167");
            var fresh = launched.AgentSessionId.ShouldNotBeNull("G-167");
            fresh.ShouldNotBe(f.SessionId, "G-167");
            await f.Harness.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(f.SessionId, CancellationToken.None);
            var afterOldTurn = await f.TaskAsync();
            afterOldTurn.Status.ShouldBe(AgentTaskStatus.Dispatched, "G-167");
            afterOldTurn.Attempt.ShouldBe(2, "G-167");
            afterOldTurn.AgentSessionId.ShouldBe(fresh, "G-167");
            await using var db = f.Db();
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Completed
                && e.AgentSessionId == fresh)).ShouldBe(0, "G-167");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            var parentId = Guid.NewGuid();
            var mergeId = Guid.NewGuid();
            var mergeSession = Guid.NewGuid();
            await using (var db = f.Db())
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = parentId, RootTaskId = parentId, Goal = "conflicted parent",
                    Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Blocked,
                    WorkingDirectory = f.Harness.TempRoot, WorktreePath = f.Harness.TempRoot,
                    WorktreeBranch = "feat/conflicted", MergeTargetRef = "master",
                    FailureReason = "Rebase onto master conflicted in 1 file(s).",
                    CreatedAt = f.Now, Attempt = 1, AgentKind = AgentKind.ClaudeCode,
                });
                db.AgentSessions.Add(new AgentSession
                {
                    Id = mergeSession, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, StartedAt = f.Now.AddMinutes(-5), Cwd = f.Harness.TempRoot,
                });
                db.AgentTasks.Add(new AgentTask
                {
                    Id = mergeId, RootTaskId = parentId, ParentTaskId = parentId, Goal = "resolve the conflict",
                    Role = AgentTaskRole.Merge, Workspace = WorkspaceMode.Worktree,
                    Status = AgentTaskStatus.Working, AgentSessionId = mergeSession,
                    WorkingDirectory = f.Harness.TempRoot, DispatchedAt = f.Now.AddMinutes(-1),
                    CreatedAt = f.Now, Attempt = 1, AgentKind = AgentKind.ClaudeCode,
                });
                await db.SaveChangesAsync();
            }

            var launches = f.Launches.Calls.Count;
            await TurnSeeding.SeedTurnAsync(() => f.Db(), mergeSession, DelegationReportFormatter.TaskMarker(mergeId),
                "Resolved the conflict and fast-forwarded master.");
            await f.Harness.Provider.GetRequiredService<AgentTaskReplyService>()
                .OnTurnEndAsync(mergeSession, CancellationToken.None);
            await using var settled = f.Db();
            (await settled.AgentTasks.SingleAsync(t => t.Id == parentId)).Status.ShouldBe(AgentTaskStatus.Succeeded, "G-168");
            (await settled.AgentTasks.SingleAsync(t => t.Id == mergeId)).Status.ShouldBe(AgentTaskStatus.Succeeded, "G-168");
            f.Launches.Calls.Count.ShouldBe(launches, "G-168");
        }
    }

    [Test]
    public async Task C1065_PinnedFollowupCannotStealParkedIdentity()
    {
        await using (var remote = await ParkedPredecessor.CreateAsync(remote: true))
        {
            var before = await remote.CountAsync();
            await using var db = remote.Kit.Context();
            var refused = await Should.ThrowAsync<ValidationException>(() => remote.Kit.Service(db).CreateAsync(
                remote.Follow(), remote.Kit.Caller, CancellationToken.None));
            refused.Code.ShouldBe("follow_up_remote_pool_unsupported", "G-170");
            refused.Message.ShouldContain("without -OnAgent", Case.Sensitive, "G-170");
            (await remote.CountAsync()).ShouldBe(before, "G-170");
        }

        await using (var local = await ParkedPredecessor.CreateAsync(remote: false))
        {
            var before = await local.CountAsync();
            await using var db = local.Kit.Context();
            var refused = await Should.ThrowAsync<ConflictException>(() => local.Kit.Service(db).CreateAsync(
                local.Follow(), local.Kit.Caller, CancellationToken.None));
            refused.Code.ShouldBe("follow_up_agent_blocked", "G-169");
            refused.Message.ShouldContain(DelegationReportFormatter.Short(local.PriorId), Case.Sensitive, "G-169");
            refused.Message.ShouldContain("delegate.ps1 -Reply", Case.Sensitive, "G-169");
            refused.Message.ShouldNotContain("/cancel", Case.Sensitive, "G-169");
            (await local.CountAsync()).ShouldBe(before, "G-169");
        }

        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await PublishAsync(f);
        await StampAsync(f);
        using (var scope = f.Harness.Provider.CreateScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(f.TaskId, CancellationToken.None);
            var blocked = detail.Blocked.ShouldNotBeNull();
            blocked.CanAnswer.ShouldBeTrue("G-169");
            blocked.Context.ShouldNotBeNull().ShouldContain("published seat was released", Case.Sensitive, "G-169");
            blocked.Context.ShouldContain("delegate.ps1 -Reply", Case.Sensitive, "G-169");
        }

        await f.AnswerAsync("same task reply");
        await using var rows = f.Db();
        (await rows.AgentTasks.CountAsync()).ShouldBe(1, "G-169");
        var resumed = await f.TaskAsync();
        resumed.AgentId.ShouldBe(f.AgentId, "G-169");
        resumed.Attempt.ShouldBe(2, "G-169");
        resumed.Status.ShouldBe(AgentTaskStatus.Queued, "G-169");
        resumed.ReleasedSeatAnswer.ShouldBe("same task reply", "G-169");
    }

    [Test]
    public async Task C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        await PublishAsync(f);
        await StampAsync(f);
        await RetireLocalSessionAsync(f);
        var before = await CountTasksAsync(f);
        var blockedShort = DelegationReportFormatter.Short(f.TaskId);

        var parked = await DetailAsync(f);
        parked.CanAnswer.ShouldBeTrue("G-7");
        parked.Context.ShouldNotBeNull().ShouldContain("published seat was released", Case.Sensitive, "G-7");
        var released = await Should.ThrowAsync<ConflictException>(() => FollowUpAsync(f));
        released.Code.ShouldBe("follow_up_agent_blocked", "G-7");
        released.Message.ShouldContain("The published seat was released", Case.Sensitive, "G-7");
        (await CountTasksAsync(f)).ShouldBe(before, "G-7");

        await AdvanceAttemptAsync(f);
        var moved = await DetailAsync(f);
        moved.CanAnswer.ShouldBeFalse("G-7");
        (moved.Context ?? "").ShouldNotContain("published seat was released", Case.Sensitive, "G-7");
        var cancel = await Should.ThrowAsync<ConflictException>(() => FollowUpAsync(f));
        cancel.Code.ShouldBe("follow_up_agent_blocked", "G-7");
        cancel.Message.ShouldContain("re-send", Case.Sensitive, "G-7");
        cancel.Message.ShouldNotContain("published seat was released", Case.Sensitive, "G-7");

        await ConfirmCurrentAttemptParkAsync(f);
        var again = await DetailAsync(f);
        again.CanAnswer.ShouldBeTrue("G-7");
        again.Context.ShouldNotBeNull().ShouldContain("published seat was released", Case.Sensitive, "G-7");
        var releasedAgain = await Should.ThrowAsync<ConflictException>(() => FollowUpAsync(f));
        releasedAgain.Code.ShouldBe("follow_up_agent_blocked", "G-7");
        releasedAgain.Message.ShouldContain("The published seat was released", Case.Sensitive, "G-7");

        await f.EditAsync((task, agent) =>
        {
            task.RunnerId = "server2";
            agent.RunnerId = "server2";
            agent.IsPoolDelegate = true;
        });
        var named = await Should.ThrowAsync<ValidationException>(() => FollowUpAsync(f));
        named.StatusCode.ShouldBe(422, "G-8");
        named.Code.ShouldBe("follow_up_remote_pool_unsupported", "G-8");
        named.Message.ShouldContain($"-Reply {blockedShort}", Case.Sensitive, "G-8");
        named.Message.ShouldContain(blockedShort, Case.Sensitive, "G-8");
        named.Message.ShouldContain("without -OnAgent", Case.Sensitive, "G-8");
        (await CountTasksAsync(f)).ShouldBe(before, "G-8");

        await RemoveCurrentAttemptParkAsync(f);
        var silent = await Should.ThrowAsync<ValidationException>(() => FollowUpAsync(f));
        silent.StatusCode.ShouldBe(422, "G-8");
        silent.Code.ShouldBe("follow_up_remote_pool_unsupported", "G-8");
        silent.Message.ShouldNotContain("-Reply", Case.Sensitive, "G-8");
        silent.Message.ShouldContain("without -OnAgent", Case.Sensitive, "G-8");
    }

    [Test]
    public async Task C1065_ParkedReviewReplyBindsFreshEvidence()
    {
        foreach (var busyCaller in new[] { true, false })
        {
            var world = await ReviewWorld.StartAsync(busyCaller);
            await using var f = world.Fixture;
            var beforeReply = await OutcomesAsync(f);
            beforeReply.Count.ShouldBe(1, "G-192");
            beforeReply.Single().ReviewedSourceSha.ShouldBeNull("G-192");
            await f.RestartAsync();
            (await OutcomesAsync(f)).Count.ShouldBe(1, "G-192");
            (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Blocked, "G-192");

            await f.AnswerAsync("report the final evidence again", round: null);
            f.Launches.Calls.ShouldBeEmpty("G-192");
            await ConfirmContinuationAsync(f, busy: true);
            await SettleReviewAsync(f, (await f.TaskAsync()).AgentSessionId!.Value, world.Report());
            var successor = await BoundSuccessorAsync(f, beforeReply.Single().Id);
            successor.ReviewedSourceSha.ShouldBe(world.Sha, "G-193");
            successor.SubjectTaskId.ShouldBe(world.SubjectId, "G-193");
            successor.SupersedesId.ShouldBe(beforeReply.Single().Id, "G-193");
            successor.ReviewedSourceClean.ShouldBe(true, "G-193");
            (await OutcomesAsync(f)).Count.ShouldBe(2, "G-193");
            await AssertCallerReceiptAsync(f, world.ParentId, successor.Id, world.Sha, busyCaller);
        }

        foreach (var shape in new[] { "missing", "wrong-subject", "dirty" })
        {
            var world = await ReviewWorld.StartAsync(busyCaller: true);
            await using var f = world.Fixture;
            var old = (await OutcomesAsync(f)).Single();
            await f.AnswerAsync("report the final evidence again", round: null);
            await ConfirmContinuationAsync(f, busy: true);
            if (shape == "dirty")
                await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "dirty-review.txt"), "unpublished");
            var sha = shape == "wrong-subject" ? new string('b', 40) : world.Sha;
            await SettleReviewAsync(f, (await f.TaskAsync()).AgentSessionId!.Value,
                world.Report(evidence: shape != "missing", sha: sha));
            var rows = await OutcomesAsync(f);
            rows.ShouldAllBe(o => o.ReviewedSourceSha == null, shape);
            rows.ShouldNotContain(o => o.SupersedesId == old.Id && o.ReviewedSourceSha == world.Sha, shape);
            var settled = await f.TaskAsync();
            settled.NextStage.ShouldBe(PipelineHandoffKind.Decide, shape);
            await using var notesDb = f.Db();
            var snapshot = (await notesDb.AgentTaskLandNotifications.AsNoTracking()
                .Where(n => n.TaskId == f.TaskId).ToListAsync())
                .Select(n => TaskCompletionNotification.TryReadSnapshot(n.CompletionSnapshotJson))
                .Single(s => s is { Status: AgentTaskStatus.Succeeded });
            snapshot!.NextStage.ShouldBe("decide", shape);
            snapshot.NoteHeader.ShouldNotContain("reviewed-sha=" + world.Sha, Case.Sensitive, shape);
            snapshot.NoteHeader.ShouldNotContain("next=land", Case.Sensitive, shape);
        }
    }

    private static string Label(string shape) => shape switch
    {
        "partial" => "G-155",
        "wrong-session" => "G-156",
        "generation" => "G-157",
        "stale" => "G-158",
        "queued" or "assistant" => "G-159",
        "ack" => "G-160",
        _ => "G-155",
    };

    private static async Task DeliverAsync(RunnerSeatReleaseFixture f, string answer, bool busy)
    {
        var tip = await PublishAsync(f);
        await StampAsync(f);
        await f.AnswerAsync(answer);
        f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy).GetAwaiter().GetResult();
        await f.DispatchAsync();
        var queued = (await f.AnswerQueueAsync()).ShouldNotBeNull("G-154");
        var spill = queued.RemoteSpillBody.ShouldNotBeNull("G-154");
        var normalized = answer.ReplaceLineEndings("\n");
        spill.Split(normalized).Length.ShouldBe(2, "G-154");
        spill.ShouldContain(Canary, Case.Sensitive, "G-154");
        spill.ShouldContain($"Prior transcript: GET /api/sessions/{f.SessionId}/transcript?since=0", Case.Sensitive, "G-154");
        spill.ShouldContain("Full report: report.md", Case.Sensitive, "G-154");
        spill.ShouldContain($"Parked source SHA: {tip}; full ref: ", Case.Sensitive, "G-154");
        if (busy) f.Recipient!.Inputs.ShouldBeEmpty("G-162");
        await f.RestartAsync();
        await f.AttachRecipientAsync(queued.AgentSessionId);
        if (busy) await f.EndTurnAsync(queued.AgentSessionId);
        await f.FlushAsync(queued.AgentSessionId);
        await f.DispatchAsync();
        var file = Path.Combine(f.Harness.TempRoot, queued.RemoteSpillRelativePath!);
        (await File.ReadAllTextAsync(file)).ShouldBe(spill, "G-154");
        f.Submitted.ShouldContain(queued.Body, "G-154");
        f.Recipient!.Inputs.Last().ShouldBe("\r", "G-154");
        (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull("G-160");
        await using var db = f.Db();
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == queued.AgentSessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == queued.Body)).ShouldBe(1, "G-155");
    }

    private static async Task ConfirmContinuationAsync(RunnerSeatReleaseFixture f, bool busy)
    {
        f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy).GetAwaiter().GetResult();
        await f.DispatchAsync();
        var queued = (await f.AnswerQueueAsync()).ShouldNotBeNull("G-194");
        await f.RestartAsync();
        await f.AttachRecipientAsync(queued.AgentSessionId);
        if (busy) await f.EndTurnAsync(queued.AgentSessionId);
        await f.FlushAsync(queued.AgentSessionId);
        await f.DispatchAsync();
        (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull("G-194");
    }

    private static Task ContinueAsync(RunnerSeatReleaseFixture f) =>
        f.Harness.Provider.GetRequiredService<AgentTaskReplyService>()
            .ContinueWithAuthorityAsync(f.TaskId, AnswerOrigin.Cli, CancellationToken.None);

    private static async Task SettleReviewAsync(RunnerSeatReleaseFixture f, Guid sessionId, string report)
    {
        await f.EditAsync((task, _) =>
        {
            task.Status = AgentTaskStatus.Working;
            task.CompletedAt = null;
            task.DispatchedAt = f.Now.AddMinutes(-1);
            task.Result = null;
            task.AgentSessionId = sessionId;
        });
        await TurnSeeding.SeedTurnAsync(() => f.Db(), sessionId, DelegationReportFormatter.TaskMarker(f.TaskId), report);
        await f.Harness.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
    }

    private static async Task<List<StageOutcome>> OutcomesAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.StageOutcomes.AsNoTracking().Where(o => o.StageTaskId == f.TaskId)
            .OrderBy(o => o.RecordedAt).ThenBy(o => o.Id).ToListAsync();
    }

    private static async Task<StageOutcome> BoundSuccessorAsync(RunnerSeatReleaseFixture f, Guid oldId)
    {
        var rows = await OutcomesAsync(f);
        return rows.Single(o => o.Id != oldId && o.ReviewedSourceSha != null);
    }

    private static async Task AssertCallerReceiptAsync(
        RunnerSeatReleaseFixture f, Guid parentId, Guid evidenceId, string sha, bool busyCaller)
    {
        await using var db = f.Db();
        var notes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == f.TaskId).ToListAsync();
        var final = notes.Single(n => TaskCompletionNotification.TryReadSnapshot(n.CompletionSnapshotJson)!.Status == AgentTaskStatus.Succeeded);
        var header = TaskCompletionNotification.TryReadSnapshot(final.CompletionSnapshotJson)!.NoteHeader;
        header.ShouldContain("review-evidence=" + evidenceId.ToString("N"), Case.Sensitive, "G-194");
        header.ShouldContain("reviewed-sha=" + sha, Case.Sensitive, "G-194");
        if (busyCaller)
        {
            await using var quiet = f.Db();
            (await quiet.TranscriptEntries.CountAsync(t => t.AgentSessionId == parentId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(evidenceId.ToString("N")))).ShouldBe(0, "G-194");
        }

        await f.AttachRecipientAsync(parentId, busy: false);
        if (busyCaller) await f.EndTurnAsync(parentId);
        using var scope = f.Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(final.Id, CancellationToken.None);
        await f.FlushAsync(parentId);
        await using var after = f.Db();
        var prompts = await after.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == parentId
            && t.Kind == TranscriptKinds.UserPrompt).Select(t => t.Text).ToListAsync();
        prompts.ShouldContain(t => t != null
            && t.Contains("review-evidence=" + evidenceId.ToString("N"), StringComparison.Ordinal)
            && t.Contains(sha, StringComparison.Ordinal), "G-194");
    }

    private static string CharAnswer(int chars) => new string('λ', chars - Canary.Length) + Canary;

    private static string ByteAnswer(int bytes)
    {
        var canaryBytes = Encoding.UTF8.GetByteCount(Canary);
        var fill = bytes - canaryBytes;
        var answer = (fill % 2 == 1 ? "x" : "") + new string('λ', fill / 2) + Canary;
        Encoding.UTF8.GetByteCount(answer).ShouldBe(bytes, "G-154");
        return answer;
    }

    private static async Task<string> PublishAsync(RunnerSeatReleaseFixture f)
    {
        await f.CreateSourceAsync();
        var branch = (await f.TaskAsync()).WorktreeBranch.ShouldNotBeNull();
        await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "parked-tip.txt"), "parked tip");
        await f.GitAsync(f.SourcePath, "add", "parked-tip.txt");
        await f.GitAsync(f.SourcePath, "commit", "-m", "parked tip");
        await f.GitAsync(f.SourcePath, "push", "--no-follow-tags", "origin", $"HEAD:refs/heads/{branch}");
        var tip = await f.GitAsync(f.SourcePath, "rev-parse", "HEAD");
        await f.EditAsync((task, _) => task.ResultFilePath = "report.md");
        using var scope = f.Harness.Provider.CreateScope();
        var task = await f.TaskAsync();
        await using var db = f.Db();
        var block = await db.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).Select(e => e.Id).FirstAsync();
        var id = (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>()
            .RegisterAsync(f.TaskId, task.Attempt, block, task.ConcurrencyToken, CancellationToken.None)).ShouldNotBeNull();
        (await scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>()
            .PrepareAsync(id, CancellationToken.None)).Evidence.ShouldNotBeNull();
        await f.HandleParkAsync();
        var park = await f.ParkAsync();
        park.PublicationReceiptId.ShouldNotBeNull();
        park.SourceSha.ShouldBe(tip);
        return tip;
    }

    private static Task StampAsync(RunnerSeatReleaseFixture f) => f.EditAsync((task, _) =>
    {
        task.RepliedAtSequence = 9;
        task.ReportNudgedAt = f.Now;
        task.ReportNudgeMessageId = Guid.NewGuid();
        task.NextCheckAt = f.Now.AddHours(1);
        task.CheckCount = 4;
        task.RemoteWorktreePath = task.WorktreePath;
    });

    private static async Task RetireLocalSessionAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
        var agent = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
        task.AgentSessionId = null;
        task.RunnerId = null;
        agent.RunnerId = null;
        session.Status = SessionStatus.Stopped;
        session.EndedAt = f.Now;
        await db.SaveChangesAsync();
    }

    private static async Task AdvanceAttemptAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
        task.Attempt = task.Attempt + 1;
        task.ConcurrencyToken = Guid.NewGuid();
        task.Status = AgentTaskStatus.Blocked;
        task.AgentSessionId = null;
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = f.TaskId, Type = AgentTaskEventType.Blocked,
            At = f.Now.AddMinutes(task.Attempt), Detail = "Which answer?",
        });
        await db.SaveChangesAsync();
    }

    private static async Task ConfirmCurrentAttemptParkAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
        var blockId = await db.AgentTaskEvents
            .Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => e.Id).FirstAsync();
        var releaseId = Guid.NewGuid();
        db.RunnerSeatReleases.Add(new RunnerSeatRelease
        {
            Id = releaseId, RunnerId = "c1103", RunnerStoreId = Guid.NewGuid(),
            SessionId = f.SessionId, AcceptedStartedAt = f.Now.AddMinutes(task.Attempt), TaskId = f.TaskId,
            Attempt = task.Attempt, AgentId = f.AgentId, State = RunnerSeatReleaseState.Confirmed,
            ConfirmedAt = f.Now, ReasonCode = "confirmed", CreatedAt = f.Now, UpdatedAt = f.Now,
        });
        db.AgentTaskParks.Add(new AgentTaskPark
        {
            Id = Guid.NewGuid(), TaskId = f.TaskId, Attempt = task.Attempt, BlockEventId = blockId,
            TaskConcurrencyToken = task.ConcurrencyToken, AgentId = f.AgentId, SessionId = f.SessionId,
            PublicationReceiptId = Guid.NewGuid(), RunnerSeatReleaseId = releaseId,
            State = AgentTaskParkState.Parked, Workspace = task.Workspace, BlockedAt = f.Now,
            ReasonCode = "parked", CreatedAt = f.Now, UpdatedAt = f.Now, ParkedAt = f.Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task RemoveCurrentAttemptParkAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        var attempt = await db.AgentTasks.Where(t => t.Id == f.TaskId).Select(t => t.Attempt).SingleAsync();
        var parks = await db.AgentTaskParks.Where(p => p.TaskId == f.TaskId && p.Attempt == attempt).ToListAsync();
        db.AgentTaskParks.RemoveRange(parks);
        await db.SaveChangesAsync();
    }

    private static async Task<BlockedContextDto> DetailAsync(RunnerSeatReleaseFixture f)
    {
        using var scope = f.Harness.Provider.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .GetAsync(f.TaskId, CancellationToken.None);
        return detail.Blocked.ShouldNotBeNull();
    }

    private static async Task FollowUpAsync(RunnerSeatReleaseFixture f)
    {
        using var scope = f.Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
            new CreateAgentTaskRequest("c1103 follow-up", Role: AgentTaskRole.Code,
                FollowUpOnTask: f.TaskId.ToString("D")),
            new AgentTaskService.Caller(null, null, f.Harness.TempRoot),
            CancellationToken.None);
    }

    private static async Task<int> CountTasksAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTasks.CountAsync();
    }

    private sealed class ReviewWorld
    {
        public required RunnerSeatReleaseFixture Fixture { get; init; }
        public required Guid SubjectId { get; init; }
        public required Guid ParentId { get; init; }
        public required string Sha { get; init; }
        public string Report(bool evidence = true, string? sha = null)
        {
            var reviewed = sha ?? Sha;
            return $"Final review.\n[antiphon-finding:{DelegationReportFormatter.Short(Fixture.TaskId)} Clean] final finding\n"
                + (evidence
                    ? $"--- review evidence ---\nsubjectTaskId: {SubjectId:D}\nreviewedSourceSha: {reviewed}\nreviewedSourceClean: true\nordinaryScopeCompleted: Full\n\n"
                    : "")
                + "--- next stage ---\nnext: land\nhandoff: final report\n";
        }

        public static async Task<ReviewWorld> StartAsync(bool busyCaller)
        {
            // CARD-1082 D-8: parked-review rebinding stays on the CARD-1065 path with sync debt off.
            var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, syncRecovery: true,
                completionSingleWriteBytes: 86_400, syncDebt: false);
            try
            {
                await f.CreateSourceAsync(remote: true);
                await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "published.txt"), "retained work");
                await f.GitAsync(f.SourcePath, "add", "published.txt");
                await f.GitAsync(f.SourcePath, "commit", "-m", "published work");
                await f.GitAsync(f.SourcePath, "push", "origin", "HEAD");
                var sha = await f.GitAsync(f.SourcePath, "rev-parse", "HEAD");
                var subjectId = Guid.NewGuid();
                var branch = "feat/subject-" + subjectId.ToString("N")[..8];
                await f.GitAsync(f.SourcePath, "push", "origin", sha + ":refs/heads/" + branch);
                var task = await f.TaskAsync();
                var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!;
                var subjectBaseline = baseline with
                {
                    Primary = baseline.Primary! with
                    {
                        OwnerTaskId = subjectId, FullRef = "refs/heads/" + branch, LocalSha = sha,
                    },
                };
                await using (var db = f.Db())
                {
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = subjectId, RootTaskId = subjectId, Goal = "code", Role = AgentTaskRole.Code,
                        Workspace = WorkspaceMode.Worktree, RepoPath = task.RepoPath,
                        WorkingDirectory = task.WorkingDirectory, WorktreeBranch = branch, WorktreeBaseSha = sha,
                        ProgressBaselineJson = TaskProgressJson.SerializeBaseline(subjectBaseline),
                        Status = AgentTaskStatus.Succeeded, CreatedAt = f.Now, Attempt = 1,
                        AgentKind = AgentKind.ClaudeCode,
                    });
                    await db.SaveChangesAsync();
                }

                var parent = await f.AddParentAsync(busyCaller);
                await f.EditAsync((row, _) =>
                {
                    row.Role = AgentTaskRole.Review;
                    row.Stage = OrchestrationStage.Review;
                    row.VerificationProfileVersion = 1;
                    row.VerificationRound = VerificationRound.Final;
                    row.FollowUpOfTaskId = subjectId;
                    row.ResultFilePath = "report.md";
                });
                var leases = f.Harness.Provider.GetRequiredService<IRepositoryMutationLease>();
                await using (var lease = await leases.TryAcquireAsync((await f.TaskAsync()).RepoPath!, CancellationToken.None))
                {
                    lease.ShouldNotBeNull();
                    f.Wire.LeaseBusyObserved = () => f.Clock.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
                    await SettleReviewAsync(f, f.SessionId, new ReviewWorld { Fixture = f, SubjectId = subjectId, ParentId = parent, Sha = sha }.Report());
                }

                (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Blocked);
                await f.HandleParkAsync();
                (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked);
                f.Clock.Advance(TimeSpan.FromMinutes(1));
                using var scope = f.Harness.Provider.CreateScope();
                (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().RecoverBlockedTaskSyncAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
                (await f.ParkAsync()).SyncState.ShouldBe(AgentTaskParkSyncState.Ready, "G-192");
                return new ReviewWorld { Fixture = f, SubjectId = subjectId, ParentId = parent, Sha = sha };
            }
            catch
            {
                await f.DisposeAsync();
                throw;
            }
        }
    }

    private sealed class ParkedPredecessor : IAsyncDisposable
    {
        public required string Scratch { get; init; }
        public required DefaultRunnerKit Kit { get; init; }
        public required IsolatedTestSchema Schema { get; init; }
        public required Guid PriorId { get; init; }

        public CreateAgentTaskRequest Follow() => new("c1065 follow-up", Role: AgentTaskRole.Code,
            FollowUpOnTask: PriorId.ToString("D"));

        public async Task<int> CountAsync()
        {
            await using var db = Kit.Context();
            return await db.AgentTasks.CountAsync();
        }

        public static async Task<ParkedPredecessor> CreateAsync(bool remote)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
            var scratch = Path.Combine(kit.RepoRoot, ".antiphon", "c1065-s8", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                await using var db = kit.Context();
                var created = await kit.Service(db).CreateAsync(new CreateAgentTaskRequest(
                    "c1065 predecessor", Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode,
                    Workspace: WorkspaceMode.Worktree, RunnerId: "server2"), kit.Caller, CancellationToken.None);
                var now = DateTime.UtcNow;
                var sessionId = Guid.NewGuid();
                var agentId = Guid.NewGuid();
                var name = "c1065-park-" + agentId.ToString("N")[..8];
                var runner = remote ? "server2" : null;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, DefinitionName = "ClaudeCode", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Stopped, Cwd = scratch, Cols = 120, Rows = 30,
                    RunnerId = remote ? "server2" : null,
                    RunnerStoreId = remote ? Guid.NewGuid() : null,
                    RunnerCwd = remote ? scratch : null,
                    StartedAt = now.AddMinutes(-10),
                    CreatedAt = now.AddMinutes(-10), LastSeenAt = now.AddMinutes(-1),
                });
                db.Agents.Add(new Agent
                {
                    Id = agentId, Name = name, Slug = name, Details = "CARD-1065 parked predecessor",
                    WorkingDirectory = scratch, Status = AgentStatus.Idle, Kind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Medium, IsPoolDelegate = true, PoolIdleSince = now,
                    PersistentSessionId = sessionId.ToString("D"), RunnerId = runner,
                    CreatedAt = now.AddMinutes(-10), UpdatedAt = now,
                });
                var prior = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
                prior.AgentId = agentId;
                prior.AgentSessionId = sessionId;
                prior.RunnerId = runner;
                prior.Status = AgentTaskStatus.Blocked;
                prior.CompletedAt = now;
                prior.Attempt = 1;
                var blockId = Guid.NewGuid();
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = blockId, AgentTaskId = prior.Id, Type = AgentTaskEventType.Blocked,
                    At = now, Detail = "Which answer?",
                });
                var releaseId = Guid.NewGuid();
                db.RunnerSeatReleases.Add(new RunnerSeatRelease
                {
                    Id = releaseId, RunnerId = remote ? "server2" : "local", RunnerStoreId = Guid.NewGuid(),
                    SessionId = sessionId, AcceptedStartedAt = now.AddMinutes(-10), TaskId = prior.Id,
                    Attempt = 1, AgentId = agentId, State = RunnerSeatReleaseState.Confirmed,
                    ConfirmedAt = now, ReasonCode = "confirmed", CreatedAt = now, UpdatedAt = now,
                });
                db.AgentTaskParks.Add(new AgentTaskPark
                {
                    Id = Guid.NewGuid(), TaskId = prior.Id, Attempt = 1, BlockEventId = blockId,
                    TaskConcurrencyToken = prior.ConcurrencyToken, AgentId = agentId, SessionId = sessionId,
                    PublicationReceiptId = Guid.NewGuid(), RunnerSeatReleaseId = releaseId,
                    State = AgentTaskParkState.Parked, Workspace = prior.Workspace, BlockedAt = now,
                    ReasonCode = "parked", CreatedAt = now, UpdatedAt = now, ParkedAt = now,
                });
                await db.SaveChangesAsync();
                return new ParkedPredecessor { Kit = kit, Schema = schema, PriorId = prior.Id, Scratch = scratch };
            }
            catch
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
                await schema.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Directory.Exists(Scratch)) Directory.Delete(Scratch, recursive: true);
            await Schema.DisposeAsync();
        }
    }

    private sealed class DeliverySaveCut : SaveChangesInterceptor
    {
        public required string Boundary { get; init; }
        public bool Armed { get; set; }
        public bool Hit { get; private set; }

        private bool Matches(DbContext db) => Boundary switch
        {
            "accept-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 1 && e.Entity.ReleasedSeatAnswerId != null),
            "attempt-before" or "attempt-after" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2 && e.Entity.Status == AgentTaskStatus.Queued),
            "queue-before" or "queue-after" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.ExecutionTaskId != null),
            "delivery-stamp" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryAttempts > 0 && e.Entity.DeliveryVerdict == null),
            "verdict-before" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryVerdict == DeliveryVerdict.Delivered),
            "complete-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2 && e.Entity.ReleasedSeatAnswer == null),
            _ => false,
        };

        private void Cut(DbContext db, bool after)
        {
            if (!Armed || Hit || (Boundary.EndsWith("after", StringComparison.Ordinal) || Boundary == "delivery-stamp") != after || !Matches(db))
                return;
            if (Boundary == "attempt-after")
            {
                CommitPending = true;
                return;
            }

            Hit = true;
            throw new InvalidOperationException($"injected {Boundary}");
        }

        public bool CommitPending { get; private set; }

        public void CutCommit()
        {
            if (!CommitPending || Hit) return;
            Hit = true;
            throw new InvalidOperationException("injected attempt-after commit");
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Cut(data.Context!, false);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            Cut(data.Context!, true);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeliveryCommitCut(DeliverySaveCut cut) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            cut.CutCommit();
            return Task.CompletedTask;
        }
    }
}

using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class TerminalRunnerSeatReleaseTests
{
    [Test]
    public async Task Long_answer_keeps_complete_content_and_spill_receipt()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        var answer = string.Concat(Enumerable.Repeat("retain λ 日本語 😀\r\n", 650)) + "FINAL-ANSWER-CANARY";
        await f.PrepareContinuationAsync();
        await f.ReleaseAsync();
        (await f.TryAnswerAsync(answer)).ShouldBeNull("the complete Unicode answer must be accepted without a bounded-detail database error");
        var accepted = await f.TaskAsync();
        f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
        await f.DispatchAsync();
        var queued = (await f.AnswerQueueAsync())!;
        queued.ShouldNotBeNull();
        queued.RemoteSpillBody.ShouldNotBeNull();
        queued.RemoteSpillBody.ShouldContain(answer.ReplaceLineEndings("\n"));
        queued.RemoteSpillBody.Split("FINAL-ANSWER-CANARY").Length.ShouldBe(2);
        queued.RemoteSpillBody.ShouldContain($"Accepted answer: {accepted.ReleasedSeatAnswerId:D}");
        f.Recipient!.Inputs.ShouldBeEmpty();
        await f.RestartAsync();
        await f.AttachRecipientAsync(queued.AgentSessionId);
        await f.EndTurnAsync(queued.AgentSessionId);
        await f.FlushAsync(queued.AgentSessionId);
        await f.DispatchAsync();
        var file = Path.Combine(f.Harness.TempRoot, queued.RemoteSpillRelativePath!);
        (await File.ReadAllTextAsync(file)).ShouldBe(queued.RemoteSpillBody);
        f.Submitted.ShouldBe(new[] { queued.Body });
        f.Recipient!.Inputs.Last().ShouldBe("\r");
        (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull("only the complete pointer receipt finishes answer recovery");
    }

    [Test]
    public async Task Accepted_answer_after_release_is_delivered_once()
    {
        foreach (var boundary in new[] { "accept-before", "accept-after", "attempt-before", "attempt-after",
                     "queue-before", "queue-after", "delivery-before", "verdict-before", "complete-before" })
        {
            var cut = new DeliverySaveCut { Boundary = boundary };
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked,
                configureDb: options => options.AddInterceptors(cut));
            await f.PrepareContinuationAsync(); await f.ReleaseAsync();
            f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
            cut.Armed = true;
            if (boundary == "accept-after") f.Harness.EventBus.ThrowOnceOnEvent = "AgentTaskChanged";
            await f.TryAnswerAsync("recover exactly once");
            if (boundary == "accept-before")
            {
                (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBeNull();
                f.Launches.Calls.ShouldBeEmpty();
                await f.AnswerAsync("recover exactly once");
            }
            var answerId = (await f.TaskAsync()).ReleasedSeatAnswerId;
            answerId.ShouldNotBeNull();
            // Restart reconstructs scoped and singleton services over the same migrated clone.
            await f.RestartAsync();
            f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
            await f.DispatchAsync();
            var target = await f.TaskAsync();
            target.Attempt.ShouldBe(2, boundary);
            target.AgentSessionId.ShouldNotBeNull(boundary);
            f.Recipient?.Inputs.ShouldBeEmpty("busy recipient must see no input before its committed turn end");
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBe(answerId, boundary);
            await f.RestartAsync();
            await f.AttachRecipientAsync(target.AgentSessionId!.Value);
            await f.DispatchAsync(); // restores a lost enqueue or acknowledgement
            var queued = await f.AnswerQueueAsync();
            queued.ShouldNotBeNull(boundary);
            await f.EndTurnAsync(target.AgentSessionId.Value);
            try { await f.FlushAsync(target.AgentSessionId.Value); }
            catch (InvalidOperationException) when (cut.Hit) { }
            await f.RestartAsync();
            await f.AttachRecipientAsync(target.AgentSessionId.Value);
            await f.FlushAsync(target.AgentSessionId.Value);
            await f.DispatchAsync();
            if (boundary == "complete-before") await f.DispatchAsync();
            if (boundary != "accept-after") cut.Hit.ShouldBeTrue($"must reach {boundary}");
            f.Submitted.Count.ShouldBe(1, boundary);
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull($"{boundary}: matching prompt completes recovery");
            await using var db = f.Db();
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == target.AgentSessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Text == queued!.Body)).ShouldBe(1, boundary);
            (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == f.TaskId)).ShouldBe(1, boundary);
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId
                && m.ExecutionTaskId == f.TaskId)).ShouldBe(0, "no delivery to the released seat");
        }
    }

    [Test]
    public async Task Answer_receipt_rejects_ack_stale_or_partial_prompt()
    {
        foreach (var shape in new[] { "ack", "wrong", "partial", "stale", "wrong-session", "queued", "assistant", "generation" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            await f.PrepareContinuationAsync(); await f.ReleaseAsync(); await f.AnswerAsync("receipt canary");
            f.Launches.OnLaunch = id => f.AttachRecipientAsync(id, busy: true).GetAwaiter().GetResult();
            await f.DispatchAsync();
            var row = (await f.AnswerQueueAsync())!;
            var task = await f.TaskAsync();
            await using var db = f.Db();
            var generation = (await db.AgentSessions.SingleAsync(s => s.Id == row.AgentSessionId)).StartedAt;
            var text = shape == "wrong" ? "different prompt" : shape == "partial" ? row.Body[..100] : row.Body;
            if (shape != "ack")
            {
                await f.NativePromptAsync(shape == "wrong-session" ? f.SessionId : row.AgentSessionId, text);
                if (shape is "queued" or "assistant")
                    await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId && t.Text == text)
                        .ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind,
                            shape == "queued" ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.AssistantText));
            }
            var max = await db.TranscriptEntries.Where(t => t.AgentSessionId == row.AgentSessionId).MaxAsync(t => t.Sequence);
            await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.Delivered)
                .SetProperty(m => m.LastDeliveryStartedAt, f.Now)
                .SetProperty(m => m.LastDeliveryGeneration, shape == "generation" ? generation.AddSeconds(-1) : generation)
                .SetProperty(m => m.LastDeliveryBaselineSequence, shape == "stale" ? max : 1));
            await f.DispatchAsync();
            (await f.TaskAsync()).ReleasedSeatAnswerId.ShouldBe(task.ReleasedSeatAnswerId, shape);
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBe("receipt canary", shape);
            // The positive witness comes from actual queue submit bytes, normalizer and ingest.
            await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Status, QueuedMessageStatus.Pending)
                .SetProperty(m => m.DeliveryVerdict, (DeliveryVerdict?)null)
                .SetProperty(m => m.LastDeliveryStartedAt, (DateTime?)null)
                .SetProperty(m => m.LastDeliveryGeneration, (DateTime?)null)
                .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null));
            await f.EndTurnAsync(row.AgentSessionId);
            await f.FlushAsync(row.AgentSessionId);
            await f.DispatchAsync();
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBeNull($"{shape}: real complete receipt finishes recovery");
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
            "delivery-before" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryAttempts > 0 && e.Entity.DeliveryVerdict == null),
            "verdict-before" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryVerdict == DeliveryVerdict.Delivered),
            "complete-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2 && e.Entity.ReleasedSeatAnswer == null),
            _ => false
        };
        private void Cut(DbContext db, bool after)
        {
            if (!Armed || Hit || Boundary.EndsWith("after") != after || !Matches(db)) return;
            Hit = true; throw new InvalidOperationException($"injected {Boundary}");
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Cut(data.Context!, false); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        { Cut(data.Context!, true); return ValueTask.FromResult(result); }
    }

    [Test]
    public async Task Answer_racing_release_preserves_one_owner()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        var recipientGate = f.Harness.Queue.GetLock(f.SessionId);
        await recipientGate.WaitAsync();
        try
        {
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.PendingDelivery,
                "release must observe the queue's actual gate, not an independent semaphore");
            f.Wire.ConditionalCommands.ShouldBe(0);
        }
        finally { recipientGate.Release(); }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = f.ReleaseAsync(async (at, _) =>
        {
            if (at != "BeforeDispatch") return;
            entered.SetResult(); await resume.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var answer = f.TryAnswerAsync("race answer");
        try
        {
            (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Blocked, "answer cannot mutate while release owns the queue gate");
        }
        finally { resume.TrySetResult(); await release; await answer; }
        var duplicate = await Task.WhenAll(f.TryAnswerAsync("race answer"), f.TryAnswerAsync("race answer"));
        var task = await f.TaskAsync();
        task.Attempt.ShouldBe(2, "one accepted answer produces exactly one target attempt");
        task.ReleasedSeatAnswer.ShouldBe("race answer");
        task.ReleasedSeatAnswerTargetAttempt.ShouldBe(2);
        await using var db = f.Db();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId)).ShouldBe(0);

        await using var uncertain = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        uncertain.Wire.DropReply = true;
        await uncertain.ReleaseAsync();
        await uncertain.TryAnswerAsync("persist through an ambiguous release");
        var held = await uncertain.TaskAsync();
        held.ReleasedSeatAnswer.ShouldBe("persist through an ambiguous release");
        held.Status.ShouldBe(AgentTaskStatus.Blocked); held.Attempt.ShouldBe(1);
        AgentTaskService.MatchesReleasedAnswerRevision(held, held.ConcurrencyToken, held.Attempt,
            held.ReleasedSeatAnswerId!.Value, held.ReleasedSeatAnswerReleaseId!.Value).ShouldBeTrue();
        AgentTaskService.MatchesReleasedAnswerRevision(held, Guid.NewGuid(), held.Attempt,
            held.ReleasedSeatAnswerId.Value, held.ReleasedSeatAnswerReleaseId.Value).ShouldBeFalse("revision alone fences an otherwise eligible answer");
        await using var heldDb = uncertain.Db();
        (await heldDb.SessionQueuedMessages.CountAsync()).ShouldBe(0, "no input to an uncertain corpse");

        await using var early = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        var reserved = await early.RunAsync();
        await early.IngestAsync(TranscriptKinds.UserPrompt, "still busy", early.Now);
        await early.AnswerAsync("answer before dispatch");
        using (var scope = early.Harness.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
                .AdvanceAsync(reserved.ReleaseId!.Value, early.Observation, default);
        early.Wire.ConditionalCommands.ShouldBe(0);
        (await early.TaskAsync()).Attempt.ShouldBe(1);
    }

    [Test]
    public async Task Answer_recovery_preserves_round_and_admission_guards()
    {
        var cut = new AnswerSaveCut();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked,
            configureDb: options => options.AddInterceptors(cut));
        await f.ReleaseAsync();
        var before = await f.TaskAsync();
        (await f.TryAnswerAsync("stale", round: 2)).ShouldBeOfType<ConflictException>();
        var stale = await f.TaskAsync(); stale.Attempt.ShouldBe(before.Attempt);
        stale.ReleasedSeatAnswerId.ShouldBeNull(); stale.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
        cut.Armed = true;
        await f.TryAnswerAsync("atomic answer");
        var after = await f.TaskAsync();
        cut.Hit.ShouldBeTrue("the test reaches the new-attempt save boundary");
        after.Attempt.ShouldBe(1, "transaction rolls back the interrupted new attempt");
        after.ReleasedSeatAnswer.ShouldBe("atomic answer", "accepted input survives a failed admission transaction");
        cut.Armed = false;
        await f.AnswerAsync("atomic answer");
        (await f.TaskAsync()).Attempt.ShouldBe(2);
    }

    [Test]
    public async Task Answer_stop_bypass_requires_the_exact_release_receipt()
    {
        foreach (var shape in new[] { "confirmed", "missing-session", "wrong-attempt", "wrong-store", "wrong-generation" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            if (shape != "missing-session") await f.ReleaseAsync();
            await using var db = f.Db();
            if (shape == "missing-session") await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync();
            if (shape == "wrong-attempt") await f.EditAsync((t, _) => t.Attempt++);
            if (shape is "wrong-store" or "wrong-generation")
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
                if (shape == "wrong-store") session.RunnerStoreId = Guid.NewGuid();
                else session.StartedAt = session.StartedAt.AddSeconds(1);
                await db.SaveChangesAsync();
            }
            await f.RetryAsync();
            f.RecordedStops.Killed.Count.ShouldBe(shape == "confirmed" ? 0 : 1, shape);
        }
    }

    [Test]
    public async Task Answer_admission_guards_preserve_the_accepted_reply()
    {
        foreach (var guard in new[] { "quota", "availability", "commit", "capacity", "workspace", "preference" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            await f.PrepareContinuationAsync();
            await f.ReleaseAsync();
            await using var db = f.Db();
            var before = await f.TaskAsync();
            if (guard == "availability")
                db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold { Id = Guid.NewGuid(), Kind = before.AgentKind,
                    ModelAlias = "*", Source = ModelAvailabilitySource.Manual, HitAt = f.Now, Reason = "held preference" });
            if (guard == "quota")
                db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample { Id = Guid.NewGuid(), Provider = before.AgentKind,
                    SubscriptionKey = before.AgentKind.ToString(), AgentSessionId = f.SessionId, RemainingPercent = 1,
                    ParseStatus = SubscriptionUsageParseStatus.Parsed, ObservedAt = f.Now, ResetsAt = f.Now.AddDays(3) });
            if (guard == "commit")
                db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = f.TaskId,
                    Type = AgentTaskEventType.CommitRecoveryStarted, Detail = "settlement-to-preserve", At = f.Now });
            await db.SaveChangesAsync();
            var refusal = await f.TryAnswerAsync("retain admission answer");
            var accepted = await f.TaskAsync();
            accepted.ReleasedSeatAnswer.ShouldBe("retain admission answer", guard);
            f.Launches.Calls.ShouldBeEmpty("the reply handler never launches a provider");
            f.RecordedStops.Killed.ShouldBeEmpty();
            if (guard is "quota" or "availability" or "commit")
            {
                refusal.ShouldNotBeNull(guard);
                accepted.Attempt.ShouldBe(1, guard);
                if (guard == "commit")
                    (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.CommitRecoveryStarted)).ShouldBe(1);
                // Removing this one guard makes the same persisted answer resumable through Retry.
                await db.ModelAvailabilityHolds.ExecuteDeleteAsync();
                await db.SubscriptionUsageSamples.ExecuteDeleteAsync();
                await db.AgentTaskEvents.Where(e => e.Type == AgentTaskEventType.CommitRecoveryStarted).ExecuteDeleteAsync();
                await f.RetryAsync();
            }
            else
            {
                refusal.ShouldBeNull(); accepted.Attempt.ShouldBe(2);
                if (guard == "capacity") f.Directory.Capacity = 0;
                if (guard == "preference") f.Directory.RefuseNewWork = true;
                if (guard == "workspace")
                {
                    using var scope = f.Harness.Provider.CreateScope();
                    var key = WorkspaceReservationKey.ForTask(before.WorktreePath, before.WorkingDirectory,
                        before.WorktreeBranch, before.RepoPath);
                    var fence = await scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>()
                        .TryAdmitConsumerAsync(new(key, WorkspaceReservationKind.HistoricalFence, Guid.NewGuid()), default);
                    fence.Accepted.ShouldBeTrue();
                }
                await f.DispatchAsync();
                f.Launches.Calls.ShouldBeEmpty(guard);
                var held = await f.TaskAsync();
                held.ReleasedSeatAnswer.ShouldBe("retain admission answer");
                held.AgentKind.ShouldBe(before.AgentKind); held.RunnerId.ShouldBe(before.RunnerId);
                if (guard is "capacity" or "preference") held.Status.ShouldBe(AgentTaskStatus.Queued, guard);
                f.Directory.Capacity = 10; f.Directory.RefuseNewWork = false;
                await db.WorkspaceUseReservations.Where(r => r.Kind == WorkspaceReservationKind.HistoricalFence).ExecuteDeleteAsync();
                // Workspace refusal can settle Failed; put the same accepted target back on its
                // queue for the positive admission witness, without manufacturing a receipt.
                if (held.Status == AgentTaskStatus.Failed)
                    await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Queued);
            }
            await f.DispatchAsync();
            f.Launches.Calls.Count.ShouldBe(1, $"{guard}: otherwise eligible reaches the real launch boundary");
            (await f.TaskAsync()).ReleasedSeatAnswer.ShouldBe("retain admission answer");
        }
    }

    [Test]
    public async Task Answer_fields_do_not_leak_into_a_later_attempt()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        await f.ReleaseAsync(); await f.TryAnswerAsync("answer-only-canary");
        var accepted = await f.TaskAsync();
        DelegationReportFormatter.BuildBrief(accepted, new DelegationSettings()).ShouldContain("answer-only-canary");
        accepted.Attempt++;
        DelegationReportFormatter.BuildBrief(accepted, new DelegationSettings()).ShouldNotContain("answer-only-canary");
        await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Failed);
        await f.RetryAsync();
        var retried = await f.TaskAsync();
        retried.ReleasedSeatAnswer.ShouldBeNull(); retried.ReleasedSeatAnswerId.ShouldBeNull();
        DelegationReportFormatter.BuildBrief(retried, new DelegationSettings()).ShouldNotContain("answer-only-canary");
    }

    [Test]
    public async Task Answer_keeps_workspace_and_report_context()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
        await f.EditAsync((t, _) => { t.WorktreePath = f.Harness.TempRoot; t.WorktreeBranch = "feat/retained";
            t.WorktreeBaseRef = "master"; t.Result = "retained report"; t.ResultFilePath = "report.md"; });
        var before = await f.TaskAsync();
        await f.ReleaseAsync();
        f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
        await f.TryAnswerAsync("continue retained work");
        var task = await f.TaskAsync();
        task.Attempt.ShouldBe(2); task.Status.ShouldBe(AgentTaskStatus.Queued); task.AgentSessionId.ShouldBeNull();
        task.WorktreePath.ShouldBe(before.WorktreePath); task.WorktreeBranch.ShouldBe(before.WorktreeBranch);
        task.WorktreeBaseRef.ShouldBe(before.WorktreeBaseRef); task.Result.ShouldBe(before.Result);
        task.ResultFilePath.ShouldBe(before.ResultFilePath); task.RootTaskId.ShouldBe(before.RootTaskId);
        task.RunnerId.ShouldBe(before.RunnerId); task.CardId.ShouldBe(before.CardId);
        f.RecordedStops.Killed.ShouldBeEmpty();
        f.Launches.Calls.ShouldBeEmpty();
        await using var db = f.Db();
        (await db.WorkspaceUseReservations.CountAsync(r => r.TaskId == task.Id && r.Active
            && r.Kind == WorkspaceReservationKind.Launch)).ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Reply_on_live_local_or_warm_session_is_unchanged()
    {
        foreach (var shape in new[] { "remote-live", "local", "warm" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked);
            if (shape != "remote-live")
            {
                await using var db = f.Db();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
                session.RunnerId = null; session.RunnerStoreId = null; session.RunnerCwd = null;
                await db.SaveChangesAsync();
                await f.EditAsync((t, a) => { t.RunnerId = null; t.Workspace = WorkspaceMode.Shared;
                    if (shape == "warm") a.PoolIdleSince = f.Now; });
            }
            await f.IngestAsync(TranscriptKinds.UserPrompt, "busy", f.Now);
            await f.AnswerAsync("same conversation");
            var task = await f.TaskAsync(); task.Attempt.ShouldBe(1); task.AgentSessionId.ShouldBe(f.SessionId);
            task.Status.ShouldBe(AgentTaskStatus.Working); task.ReleasedSeatAnswerId.ShouldBeNull();
            await using var read = f.Db();
            (await read.SessionQueuedMessages.SingleAsync()).Body.ShouldContain("same conversation");
            f.RecordedStops.Killed.ShouldBeEmpty();
        }
    }

    private sealed class AnswerSaveCut : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Hit { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2))
            { Hit = true; throw new InvalidOperationException("answer save cut"); }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task Completed_attempt_registers_release_debt(AgentTaskStatus status)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(status);
        await f.RunAsync();
        await f.RunAsync();
        await using var db = f.Db();
        var rows = await db.RunnerSeatReleases.ToListAsync();
        rows.Count.ShouldBe(1, $"{status} ledger count");
        rows[0].TaskId.ShouldBe(f.TaskId); rows[0].Attempt.ShouldBe(1);
        rows[0].RunnerStoreId.ShouldBe(f.Directory.StoreId); rows[0].SessionId.ShouldBe(f.SessionId);
        rows[0].AcceptedStartedAt.ShouldBe(f.Observation.ExpectedAcceptedStartedAt);
        rows[0].SettlementRevision.ShouldBe((await db.AgentTasks.SingleAsync()).ConcurrencyToken);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        // The one migration includes all nullable answer fields and permits full Unicode input.
        var task = await db.AgentTasks.SingleAsync();
        task.ReleasedSeatAnswer.ShouldBeNull(); task.ReleasedSeatAnswerId.ShouldBeNull();
        task.ReleasedSeatAnswerRoundId.ShouldBeNull(); task.ReleasedSeatAnswerReleaseId.ShouldBeNull();
        task.ReleasedSeatAnswerTargetAttempt.ShouldBeNull(); task.ReleasedSeatAnswerAcceptedAt.ShouldBeNull();
        var body = new string('界', 5001);
        task.ReleasedSeatAnswer = body; task.ReleasedSeatAnswerId = Guid.NewGuid();
        task.ReleasedSeatAnswerRoundId = Guid.NewGuid(); task.ReleasedSeatAnswerReleaseId = rows[0].Id;
        task.ReleasedSeatAnswerTargetAttempt = 2; task.ReleasedSeatAnswerAcceptedAt = f.Now;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        (await db.AgentTasks.SingleAsync()).ReleasedSeatAnswer.ShouldBe(body);
        // No FK cascade may erase rowless custody.
        await db.AgentTasks.ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteDeleteAsync();
        (await db.RunnerSeatReleases.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Incomplete_settlements_never_authorize_release()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        await f.EditAsync((task, _) => { task.Status = AgentTaskStatus.Working; task.CompletedAt = null; });
        await using (var writer = f.Db())
        {
            await using var tx = await writer.Database.BeginTransactionAsync();
            var t = await writer.AgentTasks.SingleAsync(); t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = f.Now.AddMinutes(-3);
            await writer.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteAttempt, "uncommitted settlement");
            await tx.RollbackAsync();
        }
        await f.EditAsync((t, _) => t.Status = AgentTaskStatus.Succeeded);
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteAttempt, "null CompletedAt");
        await f.EditAsync((t, _) => { t.Status = AgentTaskStatus.Blocked; t.CompletedAt = f.Now.AddMinutes(-3); t.Result = null; t.ReportEvidence = AgentTaskReportEvidence.Legacy; });
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.IncompleteReport, "routing hold");
        f.Wire.ConditionalCommands.ShouldBe(0);
        await using var db = f.Db(); (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0);
        await f.EditAsync((t, _) => { t.Result = "runner-sync blocked report"; t.ReportEvidence = AgentTaskReportEvidence.Marked; });
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "completed marked Blocked remains eligible");
    }

    [Test]
    public async Task Unsettled_blocked_or_queued_owner_is_preserved()
    {
        foreach (var bySession in new[] { true, false })
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await using var db = f.Db();
            var owner = new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Status = status,
                AgentSessionId = bySession ? f.SessionId : Guid.NewGuid(), AgentId = bySession ? Guid.NewGuid() : f.AgentId,
                CreatedAt = f.Now };
            db.AgentTasks.Add(owner); await db.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Owned, $"{status} owner by {(bySession ? "session" : "agent")}");
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            db.AgentTasks.Remove(owner); await db.SaveChangesAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "otherwise eligible");
        }
        await using var noAgent = await RunnerSeatReleaseFixture.CreateAsync();
        await noAgent.EditAsync((t, _) => t.AgentId = null);
        (await noAgent.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "null agent is not abandonment authority or a veto");
    }

    [Test]
    public async Task Standing_warm_and_verification_owners_are_preserved()
    {
        var variants = new (TerminalRunnerSeatDecision Decision, Action<AgentTask, Agent> Edit)[]
        {
            (TerminalRunnerSeatDecision.StandingOwner, (_, a) => a.IsPoolDelegate = false),
            (TerminalRunnerSeatDecision.AlwaysOnOwner, (_, a) => a.AlwaysOn = true),
            (TerminalRunnerSeatDecision.BoardOwner, (_, a) => a.BoardId = Guid.NewGuid()),
            (TerminalRunnerSeatDecision.SpecialistOwner, (_, a) => a.StandingSpecialistRole = AgentTaskRole.Check),
            (TerminalRunnerSeatDecision.SpecialistOwner, (_, a) => a.StandingSpecialistOwnerId = Guid.NewGuid()),
            (TerminalRunnerSeatDecision.SpecialistOwner, (t, _) => t.Role = AgentTaskRole.Check),
            (TerminalRunnerSeatDecision.WarmPool, (t, a) => { t.Workspace = WorkspaceMode.Shared; a.PoolIdleSince = DateTime.UtcNow; }),
            (TerminalRunnerSeatDecision.VerificationOwner, (_, _) => { }),
        };
        foreach (var variant in variants)
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(sourced: variant.Decision == TerminalRunnerSeatDecision.VerificationOwner);
            // A real board satisfies the FK; other variants each change only their own guard.
            await using var db = f.Db();
            if (variant.Decision == TerminalRunnerSeatDecision.BoardOwner)
            {
                var project = new Project { Id = Guid.NewGuid(), Name = "seat ownership project" };
                db.Projects.Add(project); await db.SaveChangesAsync();
                var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "seat owner" };
                db.Boards.Add(board); await db.SaveChangesAsync();
                await f.EditAsync((_, a) => a.BoardId = board.Id);
            }
            else await f.EditAsync(variant.Edit);
            (await f.RunAsync()).Decision.ShouldBe(variant.Decision);
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
            (await db.Agents.CountAsync(a => a.Id == f.AgentId)).ShouldBe(1);
        }
        await using var eligible = await RunnerSeatReleaseFixture.CreateAsync();
        (await eligible.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved);
    }

    [Test]
    public async Task Settlement_age_has_its_own_safety_margin()
    {
        foreach (var seconds in new[] { 0d, 119.999, 120, 120.001 })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await f.EditAsync((t, _) => t.CompletedAt = f.Now.AddSeconds(-seconds));
            (await f.RunAsync()).Decision.ShouldBe(seconds < 120 ? TerminalRunnerSeatDecision.SettlementTooYoung : TerminalRunnerSeatDecision.Reserved,
                $"server settlement age {seconds}; runner already qualified with an independent clock");
            f.Wire.ConditionalCommands.ShouldBe(0); // S3a stops at reservation.
        }
    }

    [Test]
    public async Task Pending_delivery_prevents_release()
    {
        var inputs = new (string Kind, QueuedMessageOrigin Origin)[]
        {
            ("brief", QueuedMessageOrigin.Delegation), ("answer", QueuedMessageOrigin.Delegation),
            ("channel", QueuedMessageOrigin.Channel), ("mention", QueuedMessageOrigin.Mention),
            ("completion", QueuedMessageOrigin.Delegation), ("continuation", QueuedMessageOrigin.System),
            ("recovery", QueuedMessageOrigin.System),
        };
        foreach (var input in inputs)
        foreach (var shape in new[] { "pending", "attempted", "held" })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync();
            await using var db = f.Db();
            var message = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                Body = $"{input.Kind}: complete delivery canary", Origin = input.Origin, CreatedAt = f.Now,
                Status = shape == "attempted" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending,
                DeliveryAttempts = shape == "attempted" ? 1 : 0,
                LastDeliveryStartedAt = shape == "attempted" ? f.Now : null,
                HoldUntil = shape == "held" ? f.Now.AddHours(1) : null };
            db.SessionQueuedMessages.Add(message); await db.SaveChangesAsync();
            var before = JsonSerializer.Serialize(message);
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.PendingDelivery, $"{input.Kind}/{shape}");
            f.Wire.ConditionalCommands.ShouldBe(0);
            (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
            db.ChangeTracker.Clear();
            JsonSerializer.Serialize(await db.SessionQueuedMessages.SingleAsync()).ShouldBe(before, "pending bytes/status/attempt evidence retained");
            await db.SessionQueuedMessages.ExecuteDeleteAsync();
            (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Reserved, "pending guard is the only veto");
        }
    }

    [Test]
    public async Task Concurrent_reservations_have_one_winner()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        // Registration first, then two independent connections race on the SAME existing row.
        f.Wire.Unsupported = true;
        await f.RunAsync();
        f.Wire.Unsupported = false;
        await using var read = f.Db();
        var release = await read.RunnerSeatReleases.SingleAsync();
        var task = await read.AgentTasks.SingleAsync();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Race()
        {
            using var scope = f.Harness.Provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
            await barrier.Task;
            return await service.TryReserveAsync(release, task, f.Wire.Qualified, CancellationToken.None);
        }
        var first = Race(); var second = Race(); barrier.SetResult();
        var winners = await Task.WhenAll(first, second);
        winners.Sum().ShouldBe(1, "exactly one revision-conditional reservation winner");
        read.ChangeTracker.Clear();
        var saved = await read.RunnerSeatReleases.SingleAsync();
        saved.Revision.ShouldBe(release.Revision + 1); saved.ActionId.ShouldNotBeNull();
    }

    [Test]
    public async Task Unsupported_server_transport_never_falls_back_to_force()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        f.Wire.Unsupported = true;
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Unsupported);
        f.Wire.ForceCommands.ShouldBe(0); f.Wire.ConditionalCommands.ShouldBe(0);
        var request = new TerminalSeatReleaseRequest(Guid.NewGuid(), f.Observation, "token");
        var routed = new RoutingSessionRunnerClient(f.Directory);
        (await routed.ReleaseTerminalSeatAsync(f.SessionId, request, default)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
        f.Wire.ForceCommands.ShouldBe(0);

        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: f.Schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync(); host.Directory.MarkRecovered(live);
        var force = 0;
        peer.Reply = frame =>
        {
            if (frame.Operation is PhoneHomeOperation.ReleaseSlot or PhoneHomeOperation.KillGeneration) force++;
            return frame.Operation is PhoneHomeOperation.ObserveTerminalSeat or PhoneHomeOperation.ReleaseTerminalSeat
                ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                    JsonSerializer.SerializeToElement(new { code = "unsupported_operation" })) : null;
        };
        f.Directory.Client = new RunnerScopedSessionRunnerClient(host.Directory, host.AllowedRunnerId);
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Unsupported);
        (await f.Directory.Client.ReleaseTerminalSeatAsync(f.SessionId, request, default)).Outcome.ShouldBe(TerminalSeatReleaseOutcome.Unsupported);
        force.ShouldBe(0, "old phone-home peer never falls back to force/generation kill");
        await using var db = f.Db(); (await db.RunnerSeatReleases.CountAsync(r => r.ActionId != null)).ShouldBe(0);
        f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>().Value.AutomaticEnabled = false;
        (await f.RunAsync()).Decision.ShouldBe(TerminalRunnerSeatDecision.Disabled);
        new TerminalRunnerSeatReleaseOptions().AutomaticEnabled.ShouldBeFalse();
    }
}

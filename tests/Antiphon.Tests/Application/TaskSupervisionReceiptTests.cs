using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public sealed class TaskSupervisionReceiptTests
{
    [Test]
    public async Task C1610_Supervision_fault_does_not_skip_completion_recovery()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        var fault = new NoticeInsertFault { Armed = true, FailSupervisionRead = true };
        await using var parent = await OpenAsync(h, h.ParentId, fault);
        var sourcedId = Guid.NewGuid();
        await using (var seed = h.Context())
        {
            var operationId = Guid.NewGuid();
            seed.AgentTaskLandings.Add(new AgentTaskLanding { Id = operationId, TaskId = h.TaskId,
                Active = false, CreatedAt = h.Clock.GetUtcNow().UtcDateTime, UpdatedAt = h.Clock.GetUtcNow().UtcDateTime });
            seed.AgentTasks.Add(new AgentTask { Id = sourcedId, RootTaskId = sourcedId,
                Title = "Sourced completion recovery", Goal = "Preserve the independent recovery pass",
                WorkingDirectory = Path.GetTempPath(), CreatedAt = h.Clock.GetUtcNow().UtcDateTime,
                Status = AgentTaskStatus.Succeeded, ParentSessionId = h.ParentId, ReplyTo = AgentTaskReplyTo.Session,
                SourceLandingOperationId = operationId,
                Result = "Complete sourced result.\n" + DelegationReportFormatter.ReportToken(sourcedId, "done") });
            await seed.SaveChangesAsync();
        }
        await Should.ThrowAsync<InvalidOperationException>(() => RecoverAsync(parent, h.Clock));
        fault.Fired.ShouldBeTrue();
        await using var db = h.Context();
        (await db.SessionQueuedMessages.AnyAsync(m => m.SourceTaskId == sourcedId
            && m.Origin == QueuedMessageOrigin.Delegation)).ShouldBeTrue(
                "completion-note recovery runs even when supervision recovery throws");
    }

    [Test]
    public async Task C1610_Live_failure_watch_backs_off_independently_of_delivery()
    {
        await using var h = await TaskCeilingHarness.CreateAsync(kind: TranscriptKinds.Thinking, lastAgeMinutes: 21);
        await h.SweepAsync();
        await using var recipient = await OpenAsync(h, h.SessionId);
        foreach (var delay in new[] { 1, 2, 4, 8, 15, 30, 60, 60 })
        {
            await RecoverAsync(recipient, h.Clock);
            await using var db = h.Context();
            var watch = await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId
                && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate);
            watch.ReportWatchClosedAt.ShouldBeNull();
            watch.NextReportScanAt.ShouldBe(h.Clock.GetUtcNow().UtcDateTime.AddMinutes(delay));
            var attempts = watch.ReportScanAttempts;
            await RecoverAsync(recipient, h.Clock);
            await db.Entry(watch).ReloadAsync();
            watch.ReportScanAttempts.ShouldBe(attempts, "delivery recovery cannot consume another scan poll");
            h.Clock.Advance(TimeSpan.FromMinutes(delay));
        }
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C1610_Closed_watch_backlog_cannot_starve_a_new_late_report()
    {
        await using var h = await TaskCeilingHarness.CreateAsync(kind: TranscriptKinds.Thinking, lastAgeMinutes: 21);
        await h.SweepAsync();
        await using var parent = await OpenAsync(h, h.ParentId);
        // Receipt the independent ordinary Failed completion before measuring the triage
        // watch's one-sweep latency. It otherwise owns the parent's first queue turn.
        await parent.MarkIdleAsync();
        await parent.Queue.OnTurnEndAsync(h.ParentId, CancellationToken.None);
        await using (var originalDb = h.Context())
        {
            var original = await originalDb.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == h.TaskId
                && m.Origin == QueuedMessageOrigin.Delegation);
            (await PromptsAsync(h, h.ParentId, 0)).Where(p => p.Text?.Contains(original.Body, StringComparison.Ordinal) == true)
                .ShouldHaveSingleItem("the original Failed completion receives its own queue turn first");
        }
        await using (var db = h.Context())
        {
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.TaskId);
            var original = await db.TaskSupervisionNotices.AsNoTracking().SingleAsync(n => n.TaskId == h.TaskId);
            for (var i = 0; i < 128; i++)
            {
                var stale = new AgentTask();
                db.Entry(stale).CurrentValues.SetValues(task);
                stale.Id = Guid.NewGuid(); stale.RootTaskId = stale.Id; stale.AgentId = null;
                stale.CreatedAt = task.CreatedAt.AddDays(-1);
                db.AgentTasks.Add(stale);
                var closed = new TaskSupervisionNotice();
                db.Entry(closed).CurrentValues.SetValues(original);
                closed.Id = Guid.NewGuid(); closed.TaskId = stale.Id;
                closed.State = TaskSupervisionNoticeState.NotApplicable;
                closed.ReportWatchClosedAt = h.Clock.GetUtcNow().UtcDateTime.AddDays(-1);
                closed.ReportWatchCloseReason = "final_transcript_scanned";
                closed.NextAttemptAt = closed.CreatedAt = closed.ReportWatchClosedAt.Value;
                db.TaskSupervisionNotices.Add(closed);
            }
            await db.SaveChangesAsync();
        }
        await h.FinishAsync();
        await parent.MarkIdleAsync();
        await RecoverAsync(parent, h.Clock);
        await using var verify = h.Context();
        var triage = await verify.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId
            && n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent);
        triage.State.ShouldBe(TaskSupervisionNoticeState.Told, "one sweep must ignore all 128 closed watches");
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("ended")]
    [Arguments("legacy")]
    public async Task C1610_Failure_watch_closes_without_a_report(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(kind: TranscriptKinds.Thinking, lastAgeMinutes: 21);
        await h.SweepAsync();
        await using var parent = await OpenAsync(h, h.ParentId);
        await using (var db = h.Context())
        {
            if (arm == "legacy")
                db.TaskSupervisionNotices.RemoveRange(await db.TaskSupervisionNotices.ToListAsync());
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status = SessionStatus.Stopped;
            session.EndedAt = h.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }
        await RecoverAsync(parent, h.Clock);
        await using (var db = h.Context())
        {
            var watch = await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId
                && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate);
            watch.ReasonCode.ShouldBe(arm == "legacy" ? "legacy_identity_unknown" : "final_transcript_scanned");
            watch.ReportWatchClosedAt.ShouldNotBeNull();
            watch.ReportWatchCloseReason.ShouldBe(watch.ReasonCode);
            watch.NextAttemptAt.ShouldBeGreaterThan(h.Clock.GetUtcNow().UtcDateTime.AddYears(1));
            (await TaskSupervisionNoticeService.Unresolved(db).AnyAsync(n => n.Id == watch.Id)).ShouldBeFalse(
                "a closed report watch must stop protecting retention");
            if (arm == "ended")
                watch.ScanFrontier.ShouldBe(await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                    .MaxAsync(t => t.Sequence));
        }
        h.Clock.Advance(TimeSpan.FromDays(5));
        await RecoverAsync(parent, h.Clock);
        if (arm == "ended")
        {
            await using (var db = h.Context())
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                session.StartedAt = h.Clock.GetUtcNow().UtcDateTime;
                session.Status = SessionStatus.Running;
                session.EndedAt = null;
                await db.SaveChangesAsync();
            }
            await h.FinishAsync();
            await RecoverAsync(parent, h.Clock);
            await using var verifyDb = h.Context();
            (await verifyDb.TaskSupervisionNotices.AnyAsync(n => n.TaskId == h.TaskId
                && n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent)).ShouldBeFalse(
                    "a resumed generation cannot reopen or attribute a closed predecessor watch");
        }
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C1610_Unknown_parent_backlog_does_not_starve_a_deliverable_notice()
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: TranscriptKinds.ToolCall, lastAgeMinutes: 6);
        await using var recipient = await OpenAsync(h, h.ParentId);
        await h.SweepAsync();
        await recipient.MarkIdleAsync();
        string expected;
        await using (var db = h.Context())
        {
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.TaskId);
            var notice = await db.TaskSupervisionNotices.AsNoTracking().SingleAsync(n => n.TaskId == h.TaskId);
            expected = notice.Body;
            // Persist legacy unknown-coordinate debt ahead of the real producer's notice.
            for (var i = 0; i < 32; i++)
            {
                var oldTask = new AgentTask();
                db.Entry(oldTask).CurrentValues.SetValues(task);
                oldTask.Id = Guid.NewGuid();
                oldTask.AgentId = null;
                oldTask.CreatedAt = task.CreatedAt.AddMinutes(-1);
                db.AgentTasks.Add(oldTask);
                var unknown = new TaskSupervisionNotice();
                db.Entry(unknown).CurrentValues.SetValues(notice);
                unknown.Id = Guid.NewGuid();
                unknown.TaskId = oldTask.Id;
                unknown.RecipientStartedAt = null;
                unknown.NextAttemptAt = h.Clock.GetUtcNow().UtcDateTime.AddHours(-1);
                db.TaskSupervisionNotices.Add(unknown);
            }
            await db.SaveChangesAsync();
        }
        await RecoverAsync(recipient, h.Clock);
        await RecoverAsync(recipient, h.Clock);
        (await PromptsAsync(h, h.ParentId, 0)).Where(p => p.Text?.Contains(expected, StringComparison.Ordinal) == true)
            .ShouldHaveSingleItem("unknown parent debt must back off so the next bounded batch can deliver");
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public Task C1610_Hold_parent_has_complete_UserPrompt(string mode) => ReceiptAsync("hold", mode);

    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public Task C1610_Failed_delegate_has_complete_UserPrompt(string mode) => ReceiptAsync("failure", mode);

    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public Task C1610_Failed_late_report_parent_has_complete_UserPrompt(string mode) => ReceiptAsync("late", mode);

    private static async Task ReceiptAsync(string purpose, string mode)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: purpose == "hold" ? TranscriptKinds.ToolCall : TranscriptKinds.Thinking,
            lastAgeMinutes: purpose == "hold" ? 6 : 21);
        var fault = new NoticeInsertFault { BodyFragment = purpose switch { "hold" => " held]",
            "failure" => "Your task is Failed.", _ => "Task remains Failed; late report for triage only" } };
        var recipientId = purpose == "failure" ? h.SessionId : h.ParentId;
        var recipient = await OpenAsync(h, recipientId, fault);
        await using var decoy = await OpenAsync(h);
        try
        {
            var floor = await recipient.CurrentTranscriptMaxSequenceAsync();
            await h.SweepAsync();
            if (purpose == "late")
            {
                // The ordinary Failed caller receipt already exists. It must neither
                // discharge the triage purpose nor occupy its first eligible queue turn.
                await recipient.MarkIdleAsync();
                await recipient.Queue.OnTurnEndAsync(recipientId, CancellationToken.None);
                await using (var db = h.Context())
                {
                    var original = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == h.TaskId
                        && m.Origin == QueuedMessageOrigin.Delegation);
                    (await PromptsAsync(h, recipientId, floor)).Where(p => p.Text?.Contains(original.Body, StringComparison.Ordinal) == true)
                        .ShouldHaveSingleItem("the original Failed note is independently received first");
                }
                floor = await recipient.CurrentTranscriptMaxSequenceAsync();
                await h.FinishAsync();
            }
            var before = await h.TaskAsync();
            before.Status.ShouldBe(purpose == "hold" ? AgentTaskStatus.Working : AgentTaskStatus.Failed);
            if (mode == "busy") await recipient.MarkWorkingAsync();
            else await recipient.MarkIdleAsync();
            var expectedPurpose = purpose switch { "hold" => TaskSupervisionNoticePurpose.CeilingParent,
                "failure" => TaskSupervisionNoticePurpose.FailureDelegate, _ => TaskSupervisionNoticePurpose.LateFailedParent };
            fault.Armed = mode == "crash";
            await RecoverAsync(recipient, h.Clock);
            if (mode == "crash")
            {
                fault.Fired.ShouldBeTrue("a durable obligation must survive the first queue-insert fault");
                await using (var db = h.Context())
                    (await db.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId && n.Purpose == expectedPurpose))
                        .ShouldBe(1, "producer obligation committed before enqueue");
                await recipient.DisposeAsync();
                recipient = await OpenAsync(h, recipientId);
                h.Clock.Advance(TimeSpan.FromMinutes(2));
            }
            await RecoverAsync(recipient, h.Clock);
            if (mode == "busy")
            {
                (await PromptsAsync(h, recipientId, floor)).ShouldBeEmpty("no mid-turn supervision input");
                await using (var db = h.Context())
                    (await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId && n.Purpose == expectedPurpose))
                        .State.ShouldBe(TaskSupervisionNoticeState.Pending);
                await recipient.MarkIdleAsync();
                await recipient.Queue.OnTurnEndAsync(recipientId, CancellationToken.None);
                h.Clock.Advance(TimeSpan.FromMinutes(2));
            }
            await RecoverAsync(recipient, h.Clock);
            TaskSupervisionNotice notice;
            await using (var db = h.Context())
                notice = (await db.TaskSupervisionNotices.Where(n => n.TaskId == h.TaskId && n.Purpose == expectedPurpose).ToListAsync())
                    .ShouldHaveSingleItem("a real producer must persist its independently keyed obligation");
            var prompts = await PromptsAsync(h, recipientId, floor);
            var receipt = prompts.Where(p => p.Text?.Contains(notice.Body, StringComparison.Ordinal) == true).ShouldHaveSingleItem();
            receipt.Text.ShouldContain(h.TaskId.ToString("D"));
            receipt.Sequence.ShouldBeGreaterThan(floor);
            if (purpose == "hold") receipt.Text.ShouldContain("next=decide");
            else if (purpose == "failure") receipt.Text.ShouldContain("Your task is Failed. Stop starting further work");
            else
            {
                receipt.Text.ShouldContain("Task remains Failed; late report for triage only");
                receipt.Text.ShouldContain("next=decide");
                receipt.Text.ShouldContain("Completed the authorized work");
                (await h.TaskAsync()).Result.ShouldBe(before.Result);
                (await h.TaskAsync()).CompletedAt.ShouldBe(before.CompletedAt);
                (await h.TaskAsync()).FailureReason.ShouldBe(before.FailureReason);
                var after = await h.TaskAsync();
                after.Status.ShouldBe(AgentTaskStatus.Failed);
                after.NextStage.ShouldBe(before.NextStage);
                after.NextHandoff.ShouldBe(before.NextHandoff);
                await using var outcomeDb = h.Context();
                (await outcomeDb.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.TaskId
                    && e.Type == AgentTaskEventType.Failed)).ShouldBe(1);
                (await outcomeDb.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.TaskId
                    && e.Type == AgentTaskEventType.Completed)).ShouldBeFalse();
            }
            notice.State.ShouldBe(TaskSupervisionNoticeState.Told);
            notice.ConfirmingPromptSequence.ShouldBe(receipt.Sequence);
            await RecoverAsync(recipient, h.Clock);
            (await PromptsAsync(h, recipientId, floor)).Count(p => p.Text?.Contains(notice.Body, StringComparison.Ordinal) == true).ShouldBe(1);
            (await PromptsAsync(h, decoy.SessionId, 0)).ShouldBeEmpty();
            decoy.Adapter.SubmittedBodies.ShouldBeEmpty();
            recipient.Adapter.Killed.ShouldBeFalse();
            decoy.Adapter.Killed.ShouldBeFalse();
            await using (var db = h.Context())
            {
                (await db.StageOutcomes.CountAsync()).ShouldBe(0);
                (await db.PipelineHandoffActions.CountAsync()).ShouldBe(0);
                (await db.AgentTasks.CountAsync()).ShouldBe(1);
            }
            h.Stopper.Killed.ShouldBeEmpty();
        }
        finally { await recipient.DisposeAsync(); }
    }

    [Test]
    [Arguments("retry")]
    [Arguments("generation-replacement")]
    [Arguments("settled-hold")]
    public async Task C1610_Stale_notice_is_not_delivered(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await using var parent = await OpenAsync(h, h.ParentId);
        await parent.MarkWorkingAsync();
        await h.SweepAsync();
        await RecoverAsync(parent, h.Clock);
        if (arm == "settled-hold") await h.FinishAsync();
        else if (arm == "retry") await h.MutateAsync(t => t.Attempt++);
        else
        {
            await using var db = h.Context();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.StartedAt = session.StartedAt.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        await parent.MarkIdleAsync();
        await using (var db = h.Context())
        {
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskSupervisionNoticeId != null);
            if (arm == "retry")
            {
                // Recovery must not press Enter or retry an old, interrupted typed attempt.
                message.Status = QueuedMessageStatus.Sent;
                message.DeliveryAttempts = 1;
                message.LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-10);
                message.SentAt = message.LastDeliveryStartedAt;
                message.LastDeliveryGeneration = (await db.AgentSessions.SingleAsync(s => s.Id == parent.SessionId)).StartedAt;
                message.LastDeliveryBaselineSequence = 0;
                await db.SaveChangesAsync();
            }
            else await parent.Queue.SendNowAsync(parent.SessionId, message.Id, CancellationToken.None);
        }
        await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
        await RecoverAsync(parent, h.Clock);
        var prompts = await PromptsAsync(h, parent.SessionId, 0);
        prompts.Any(p => p.Text?.Contains("[task " + DelegationReportFormatter.Short(h.TaskId) + " held]", StringComparison.Ordinal) == true)
            .ShouldBeFalse("stale hold guidance must not consume the next turn");
        await using (var db = h.Context())
            (await db.TaskSupervisionNotices.SingleAsync(n => n.TaskId == h.TaskId)).State.ShouldBe(TaskSupervisionNoticeState.Superseded);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("retry")]
    [Arguments("generation-reuse")]
    [Arguments("pre-failure-report")]
    public async Task C1610_Late_report_identity_is_fenced(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role: AgentTaskRole.Deploy,
            kind: TranscriptKinds.Thinking, lastAgeMinutes: 21);
        await using var parent = await OpenAsync(h, h.ParentId);
        if (arm == "pre-failure-report")
        {
            await h.AppendAsync(TranscriptKinds.AssistantText, "Already complete.\n" + DelegationReportFormatter.ReportToken(h.TaskId, "done"));
            // Persist a complete pre-failure report without calling the settlement path.
            await h.AppendAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            // Retain an actually overdue model-wait tail for the production failure path.
            h.Clock.Advance(TimeSpan.FromMinutes(21));
            await h.AppendAsync(TranscriptKinds.Thinking, "waiting");
            h.Clock.Advance(TimeSpan.FromMinutes(21));
        }
        await h.SweepAsync();
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        if (arm == "retry") await h.MutateAsync(t => t.Attempt++);
        else if (arm == "generation-reuse")
        {
            await using var db = h.Context();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.StartedAt = session.StartedAt.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        if (arm != "pre-failure-report") await h.FinishAsync();
        await RecoverAsync(parent, h.Clock);
        await RecoverAsync(parent, h.Clock);
        await using (var db = h.Context())
        {
            (await db.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate))
                .ShouldBe(1, "the real failure writer must record its accepted generation");
            (await db.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId && n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent))
                .ShouldBe(0, "foreign-attempt, reused-generation and pre-failure evidence cannot be forwarded");
        }
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("dispatcher")]
    [Arguments("conditional-arm2")]
    [Arguments("no-report")]
    [Arguments("api-error")]
    [Arguments("marked-failed")]
    [Arguments("check-compaction")]
    public async Task C1610_Failure_writer_records_notice(string writer)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(kind: TranscriptKinds.Thinking, lastAgeMinutes: 21);
        var fault = new NoticeInsertFault { Armed = true, BodyFragment = "Your task is Failed." };
        var recipient = await OpenAsync(h, h.SessionId, fault);
        try
        {
            if (writer == "dispatcher") await h.SweepAsync();
            else if (writer == "marked-failed") await h.FinishAsync("failed");
            else if (writer == "conditional-arm2")
            {
                await h.MutateAsync(t => t.Status = AgentTaskStatus.Dispatched);
                var db = (Antiphon.Server.Infrastructure.Data.AppDbContext)typeof(AgentTaskDispatcher)
                    .GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(h.Dispatcher)!;
                var task = await db.AgentTasks.SingleAsync(t => t.Id == h.TaskId);
                var observedType = typeof(AgentTaskDispatcher).GetNestedType("Arm2Attempt", BindingFlags.NonPublic)!;
                var observed = Activator.CreateInstance(observedType, task.Id, task.AgentSessionId,
                    task.ConcurrencyToken, task.DispatchedAt, task.RepliedAtSequence);
                await InvokeAsync(h.Dispatcher, "TryFailArm2ConditionalAsync", task, "writer witness",
                    Guid.NewGuid(), observed, CancellationToken.None);
            }
            else
            {
                await using var db = h.Context();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == h.TaskId);
                if (writer == "no-report")
                    await InvokeAsync(h.Replies, "FailUnreportedTurnAsync", h.Services, db, task, h.SessionId, CancellationToken.None);
                else if (writer == "api-error")
                {
                    var stubType = typeof(AgentTaskReplyService).GetNestedType("ApiErrorStubFacts", BindingFlags.NonPublic)!;
                    var stub = Activator.CreateInstance(stubType, "authentication_failed", 401, "Please log in", 3L, null);
                    await InvokeAsync(h.Replies, "HandleApiErrorTurnAsync", h.Services, db, task, h.SessionId, stub, CancellationToken.None);
                }
                else
                {
                    var service = new CheckCompactionContinuationService(db, h.Clock, Options.Create(new DelegationSettings()),
                        new CheckCompactionContinuationGate(), NullLogger<CheckCompactionContinuationService>.Instance);
                    await InvokeAsync(service, "FailOccupantAsync", task,
                        AgentTaskFailureCode.CompactionRecoveryRetiredGeneration, "writer witness", CancellationToken.None);
                }
            }
            (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Failed, writer);
            TaskSupervisionNotice notice;
            await using (var db = h.Context())
            {
                notice = (await db.TaskSupervisionNotices.Where(n => n.TaskId == h.TaskId
                    && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate).ToListAsync()).ShouldHaveSingleItem(writer);
                (await db.AgentTaskEvents.AnyAsync(e => e.Id == notice.SourceEventId && e.Type == AgentTaskEventType.Failed)).ShouldBeTrue(writer);
                notice.State.ShouldBe(TaskSupervisionNoticeState.Pending, "a committed failure is not a prompt receipt");
                notice.SessionId.ShouldBe(h.SessionId);
                notice.Attempt.ShouldBe(1);
                notice.SessionStartedAt.ShouldNotBeNull();
            }
            await RecoverAsync(recipient, h.Clock);
            fault.Fired.ShouldBeTrue("queue insertion fails after the writer's transaction committed");
            await recipient.DisposeAsync();
            recipient = await OpenAsync(h, h.SessionId);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            await recipient.MarkIdleAsync();
            await RecoverAsync(recipient, h.Clock);
            await RecoverAsync(recipient, h.Clock);
            await using (var db = h.Context())
            {
                var recovered = await db.TaskSupervisionNotices.SingleAsync(n => n.Id == notice.Id);
                recovered.State.ShouldBe(TaskSupervisionNoticeState.Told);
                var receipt = (await PromptsAsync(h, h.SessionId, notice.RecipientFloor))
                    .Where(p => p.Text?.Contains(notice.Body, StringComparison.Ordinal) == true).ShouldHaveSingleItem();
                recovered.ConfirmingPromptSequence.ShouldBe(receipt.Sequence);
            }
            h.Stopper.Killed.ShouldBeEmpty();
        }
        finally { await recipient.DisposeAsync(); }
    }

    private static Task InvokeAsync(object target, string method, params object?[] arguments) =>
        (Task)target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments)!;

    private static async Task<BridgeQueueHarness> OpenAsync(TaskCeilingHarness h, Guid? session = null, NoticeInsertFault? fault = null)
    {
        var bridge = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = h.ConnectionString, PreserveDatabaseOnDispose = true, ClaudeDialogObservation = true,
            AttachSessionId = session, AttachAgentId = session is null ? null : Guid.NewGuid(),
            ConfigureDbContext = fault is null ? null : b => b.AddInterceptors(fault),
        });
        bridge.Adapter.OnSubmitted = async body =>
        {
            var sequence = await bridge.CurrentTranscriptMaxSequenceAsync() + 1;
            var parts = Antiphon.SessionRunner.TranscriptNormalizer.Normalize(JsonSerializer.Serialize(new
            {
                type = "user", uuid = Guid.NewGuid().ToString("D"), timestamp = DateTimeOffset.UtcNow,
                message = new { role = "user", content = body },
            }));
            var entries = parts.Select(p => new SessionRunnerTranscriptEvent(bridge.SessionId, sequence++, p.Kind,
                p.Uuid, p.ParentUuid, p.Timestamp, p.Role, p.Text, p.ToolName, p.ToolInput, p.ToolUseId,
                p.ToolIsError, p.StopReason)).ToList();
            entries.Add(new SessionRunnerTranscriptEvent(bridge.SessionId, sequence, TranscriptKinds.TurnEnd,
                Guid.NewGuid().ToString("D"), null, null, "assistant", null, null, null, null, null, "end_turn"));
            await bridge.Runtime.PersistTranscriptAsync(bridge.SessionId, entries, CancellationToken.None);
        };
        return bridge;
    }

    private static async Task RecoverAsync(BridgeQueueHarness h, TimeProvider clock)
    {
        var worker = new CompletionNoteWorkHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            new CompletionNoteFlushQueue(), new SpecialistFailureQueue(), clock,
            NullLogger<CompletionNoteWorkHostedService>.Instance);
        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        await (Task)typeof(CompletionNoteWorkHostedService).GetMethod("RecoverMissingAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(worker, [scope.ServiceProvider, db, null, CancellationToken.None])!;
    }

    private static async Task<List<TranscriptEntry>> PromptsAsync(TaskCeilingHarness h, Guid session, long floor)
    {
        await using var db = h.Context();
        return await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == session
            && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor).ToListAsync();
    }

    private sealed class NoticeInsertFault : DbCommandInterceptor
    {
        public string? BodyFragment { get; init; }
        public bool Armed { get; set; }
        public bool FailSupervisionRead { get; init; }
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Fired && FailSupervisionRead
                && command.CommandText.Contains("FROM \"TaskSupervisionNotices\"", StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("C1610 supervision read fault");
            }
            if (Armed && !Fired && command.CommandText.Contains("INSERT INTO \"SessionQueuedMessages\"", StringComparison.Ordinal)
                && (BodyFragment is null || command.Parameters.Cast<DbParameter>().Any(p => p.Value is string text
                    && text.Contains(BodyFragment, StringComparison.Ordinal))))
            {
                Fired = true;
                throw new InvalidOperationException("C1610 post-commit queue insertion fault");
            }
            return ValueTask.FromResult(result);
        }
    }
}

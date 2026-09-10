using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.E2E;

[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel("C467LandDelivery")]
public class AgentTaskLandDeliveryE2ETests
{
    [Test]
    public async Task C467_V22_AlreadyIdleGetsOutcomeWithoutNewInput()
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync();
        await f.RequestAsync();
        await f.ReleaseExecutionAsync();
        var note = await f.ReceiptAsync();
        await f.AssertRemoteAsync();
        await f.AssertOnePromptAsync(note);
    }

    [Test]
    public async Task C467_V23_BusyCallerDoesNotBlockAnotherLand()
    {
        await using var busy = new LandDeliveryFixture();
        await busy.InitializeAsync(busy: true);
        await busy.RequestAsync();
        await busy.ReleaseExecutionAsync();
        await LandDeliveryFixture.UntilAsync(async () => {
            await using var db = busy.CreateContext();
            return await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == busy.TaskId && n.Kind == LandNotificationKind.Outcome && n.QueueMessageId != null);
        }, "busy caller has queued outcome");
        await using var idle = new LandDeliveryFixture(busy);
        await idle.InitializeAsync();
        await idle.RequestAsync(initial: false); await idle.ReleaseExecutionAsync();
        await idle.ReceiptAsync(); await idle.AssertRemoteAsync();
        await using (var db = busy.CreateContext())
        {
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == busy.TaskId && n.Kind == LandNotificationKind.Outcome);
            note.ConfirmedAt.ShouldBeNull();
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId)).DeliveryAttempts.ShouldBe(0);
        }
        await busy.ReleaseBusyAsync();
        var received = await busy.ReceiptAsync(); await busy.AssertRemoteAsync();
        await busy.AssertOnePromptAsync(received);
    }

    [Test]
    public async Task C467_V24_BlockedWriterThenReleaseDeliversBothNotes()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(); await f.UseChildAsync("none");
        var writer = Guid.NewGuid();
        await using (var db = f.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask { Id = writer, RootTaskId = writer, Title = "owned blocked writer", Goal = "guard",
                Status = AgentTaskStatus.Blocked, Workspace = WorkspaceMode.Shared, WorkingDirectory = f.Repository,
                RepoPath = f.Repository, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        var held = await f.ReceiptAsync(LandNotificationKind.Held);
        f.PublicationMutationCount().ShouldBe(0);
        (await f.AttentionAsync()).Items.ShouldContain(i => i.Kind == AttentionKind.LandHeld && i.TaskId == f.TaskId && i.HoldingTaskId == writer);
        await f.AdvanceLandClockAsync(901);
        await LandDeliveryFixture.UntilAsync(async () => {
            await using var db = f.CreateContext();
            return await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Aged && n.ConfirmedAt != null) == 2;
        }, "both hold age receipts");
        await f.SnapshotAsync(); await f.KillChildAsync(); await f.UseChildAsync("none");
        (await f.AttentionAsync()).Items.ShouldContain(i => i.Kind == AttentionKind.LandHeld && i.LandRequestId == held.RequestId && i.HoldingTaskId == writer);
        await using (var db = f.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == f.TaskId);
            request.Attempt.ShouldBe(0); request.HoldingTaskId.ShouldBe(writer);
            (await db.AgentTaskLandings.CountAsync(o => o.TaskId == f.TaskId)).ShouldBe(0);
            await db.AgentTasks.Where(t => t.Id == writer).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        }
        var outcome = await f.ReceiptAsync(); await f.AssertRemoteAsync();
        await using (var db = f.CreateContext())
        {
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.HeldReleased)).ShouldBeTrue();
            (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == f.TaskId)).HoldReasonCode.ShouldBeNull();
        }
        await f.AssertOnePromptAsync(held); await f.AssertOnePromptAsync(outcome);
        (await f.AttentionAsync()).Items.ShouldNotContain(i => i.TaskId == f.TaskId && (i.Kind == AttentionKind.LandHeld || i.LandNotificationId == outcome.Id));
    }

    [Test]
    public async Task C467_V25_HardCrashAfterOutcomeCommitRecoversReceipt()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(); await f.UseChildAsync("terminal");
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        await LandDeliveryFixture.UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(f.Root, "terminal-committed.barrier.json"))), "terminal committed crash cut");
        await using (var db = f.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == f.TaskId);
            request.IsPending.ShouldBeFalse(); request.TerminalEventId.ShouldNotBeNull();
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == request.TerminalEventId);
            note.QueueMessageId.ShouldBeNull();
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(0);
        }
        var mutations = f.PublicationMutationCount();
        await f.AssertRemoteAsync(); await f.SnapshotAsync(); await f.KillChildAsync(); await f.UseChildAsync("none");
        var received = await f.ReceiptAsync(); await f.AssertOnePromptAsync(received);
        f.PublicationMutationCount().ShouldBe(mutations, "recovery must not repeat push or cleanup");
    }

    [Test]
    public async Task C467_V26_HardCrashAfterQueueInsertReusesRow()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(busy: true); await f.UseChildAsync("queue");
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        await LandDeliveryFixture.UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(f.Root, "queue-inserted.barrier.json"))), "queue inserted crash cut");
        Guid queueId;
        await using (var db = f.CreateContext())
        {
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Outcome);
            note.QueueMessageId.ShouldBeNull();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id);
            queueId = row.Id; row.DeliveryAttempts.ShouldBe(0);
        }
        await f.SnapshotAsync(); await f.KillChildAsync(); await f.UseChildAsync("none"); await f.ReleaseBusyAsync();
        var received = await f.ReceiptAsync(); received.QueueMessageId.ShouldBe(queueId);
        Directory.GetFiles(f.Root, "queue-existing-key-*.observation.json").ShouldNotBeEmpty();
        await f.AssertRemoteAsync(); await f.AssertOnePromptAsync(received);
    }

    [Test]
    public async Task C467_V27_LostRequestWakeupRecoversAtBoot()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(); await f.UseChildAsync("lost-request");
        var id = await f.RequestAsync();
        await using var before = f.CreateContext(); var age = (await before.AgentTaskLandRequests.SingleAsync(r => r.Id == id)).RequestedAt;
        await f.KillChildAsync(); await f.ReleaseExecutionAsync(); await f.UseChildAsync("none");
        var note = await f.ReceiptAsync(); note.RequestId.ShouldBe(id);
        await using var after = f.CreateContext(); (await after.AgentTaskLandRequests.SingleAsync(r => r.Id == id)).RequestedAt.ShouldBe(age);
        await f.AssertRemoteAsync(); await f.AssertOnePromptAsync(note);
    }

    [Test]
    public async Task C467_V28_LostFlushWakeupRecoversOnIdleCaller()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(cut: "lost-flush");
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        var note = await f.ReceiptAsync(); await f.AssertRemoteAsync(); await f.AssertOnePromptAsync(note);
        Directory.GetFiles(f.Root, "completion-scan-*.observation.json").ShouldNotBeEmpty();
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("verdict")]
    public async Task C467_V30_ReceiptSaveFailureNeverRetypes(string cut)
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(); await f.UseChildAsync(cut);
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        var barrier = cut == "receipt" ? "receipt-before-save" : "queue-before-verdict";
        await LandDeliveryFixture.UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(f.Root, barrier + ".barrier.json"))), "native prompt persisted before receipt/verdict save");
        await using (var db = f.CreateContext())
        {
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Outcome);
            if (cut == "receipt") note.ConfirmedAt.ShouldBeNull();
            (await db.TranscriptEntries.AnyAsync(p => p.AgentSessionId == f.CallerId && p.Kind == TranscriptKinds.UserPrompt && p.Text!.Contains(note.Id.ToString("N")))) .ShouldBeTrue();
        }
        await f.SnapshotAsync(); await f.KillChildAsync(); await f.UseChildAsync("none");
        var received = await f.ReceiptAsync(); await f.AssertOnePromptAsync(received);
    }

    [Test]
    [Arguments("landed")]
    [Arguments("already-present")]
    [Arguments("residue-cleanup")]
    [Arguments("preoperation-refusal")]
    [Arguments("operation-refusal")]
    [Arguments("conflict")]
    public async Task C467_V29_RealOutcomeProducerMatrix(string outcome)
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(); await f.ArrangeOutcomeAsync(outcome);
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        var note = await f.ReceiptAsync(outcome == "conflict" ? LandNotificationKind.Conflict : LandNotificationKind.Outcome);
        await using (var db = f.CreateContext())
        {
            var terminal = await db.AgentTaskEvents.SingleAsync(e => e.Id == note.SourceEventId);
            terminal.Type.ShouldBe(outcome switch {
                "already-present" => AgentTaskEventType.AlreadyPresent,
                "residue-cleanup" => AgentTaskEventType.LandedWithResidue,
                "preoperation-refusal" or "operation-refusal" => AgentTaskEventType.LandRefused,
                "conflict" => AgentTaskEventType.Conflicted,
                _ => AgentTaskEventType.Landed });
            if (outcome == "conflict")
            {
                (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == note.RequestId)).State.ShouldBe(LandRequestState.NeedsResolution);
                (await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId)).Status.ShouldBe(AgentTaskStatus.Blocked);
            }
        }
        if (outcome is "landed" or "already-present" or "residue-cleanup") await f.AssertRemoteAsync();
        if (outcome is "preoperation-refusal" or "operation-refusal" or "conflict") await f.AssertSourceProtectedAsync();
        if (outcome is "preoperation-refusal" or "operation-refusal")
        {
            Directory.Exists(f.Source).ShouldBeTrue("refusal must retain the source worktree");
            var retry = await f.RequestAsync(initial: false);
            retry.ShouldNotBe(note.RequestId);
            var repeated = await f.ReceiptAsync(requestId: retry);
            repeated.Id.ShouldNotBe(note.Id);
            repeated.QueueMessageId.ShouldNotBe(note.QueueMessageId);
            await f.AssertOnePromptAsync(repeated);
        }
        if (outcome == "residue-cleanup")
        {
            File.Delete(Path.Combine(f.Source, ".antiphon", "valuable.txt"));
            var retry = await f.RequestAsync(initial: false);
            var cleanup = await f.ReceiptAsync(requestId: retry);
            cleanup.Id.ShouldNotBe(note.Id); cleanup.ContentDigest.ShouldNotBe(note.ContentDigest);
            cleanup.LandingOperationId.ShouldBe(note.LandingOperationId);
            await f.AssertOnePromptAsync(cleanup);
        }
        await f.AssertOnePromptAsync(note);
    }

    [Test]
    public async Task C467_V31_EnqueueFailureRecoversAutomatically()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(cut: "enqueue-errors");
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        Guid original = default;
        await LandDeliveryFixture.UntilAsync(async () => {
            await using var db = f.CreateContext();
            var note = await db.AgentTaskLandNotifications.FirstOrDefaultAsync(n => n.TaskId == f.TaskId && n.State == LandNotificationState.RetryPending && n.EnqueueAttempts == 2);
            if (note is null) return false;
            note.LastErrorCode.ShouldContain("IOException"); note.ConfirmedAt.ShouldBeNull(); note.QueueMessageId.ShouldBeNull();
            note.NextAttemptAt.ShouldBe(note.LastErrorAt!.Value.AddSeconds(10)); original = note.Id; return true;
        }, "two persisted enqueue failures");
        await f.AssertRemoteAsync();
        var received = await f.ReceiptAsync(); received.Id.ShouldBe(original); await f.AssertOnePromptAsync(received);
    }

    [Test]
    [Arguments("busy")]
    [Arguments("attempt")]
    public async Task C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(string state)
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(busy: state == "busy", cut: state == "attempt" ? "attempt" : "none");
        await f.UseChildAsync(state == "attempt" ? "attempt" : "none");
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        await LandDeliveryFixture.UntilAsync(async () => {
            await using var db = f.CreateContext();
            return await db.AgentTaskLandNotifications.AnyAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Outcome && n.QueueMessageId != null)
                && (state == "busy" || File.Exists(Path.Combine(f.Root, "queue-before-typing.barrier.json")));
        }, "unreceived outcome held at actual queue");
        await f.AdvanceLandClockAsync(301); await f.StatusAsync();
        await f.AdvanceLandClockAsync(901); await f.StatusAsync();
        await LandDeliveryFixture.UntilAsync(async () => {
            await using var db = f.CreateContext();
            return await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Aged) == 2;
        }, "outcome warning and error obligations");
        var attention = await f.AttentionAsync();
        var owed = attention.Items.Single(i => i.TaskId == f.TaskId && i.Kind == AttentionKind.LandOutcomeUnconfirmed && i.Headline.Contains("Outcome"));
        owed.Severity.ShouldBe(AlertSeverity.Error);
        owed.Headline.ShouldContain(state == "busy" ? "busy" : "attempted");
        await f.SnapshotAsync();
        await f.KillChildAsync();
        await f.UseChildAsync(state == "attempt" ? "attempt" : "none");
        await f.StatusAsync();
        var afterRestart = (await f.AttentionAsync()).Items.Single(i => i.LandNotificationId == owed.LandNotificationId && i.Kind == AttentionKind.LandOutcomeUnconfirmed);
        afterRestart.Severity.ShouldBe(AlertSeverity.Error);
        await using (var db = f.CreateContext())
        {
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Outcome);
            note.ConfirmedAt.ShouldBeNull();
            (await db.TranscriptEntries.AnyAsync(p => p.AgentSessionId == f.CallerId && p.Kind == TranscriptKinds.UserPrompt && p.Text!.Contains("[land " + note.Id.ToString("N")))).ShouldBeFalse();
        }
        if (state == "busy") await f.ReleaseBusyAsync(); else await f.ReleaseBoundaryAsync("queue-before-typing");
        var received = await f.ReceiptAsync(); await f.AssertOnePromptAsync(received);
        await f.UseChildAsync("none");
        (await f.AttentionAsync()).Items.ShouldNotContain(i => i.LandNotificationId == received.Id);
    }
}

using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

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
        await using var idle = new LandDeliveryFixture();
        await idle.InitializeAsync();
        await idle.RequestAsync(); await idle.ReleaseExecutionAsync();
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
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync();
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
        await f.AssertRemoteAsync(); await f.SnapshotAsync(); await f.KillChildAsync(); await f.UseChildAsync("none");
        var received = await f.ReceiptAsync(); await f.AssertOnePromptAsync(received);
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
    }

    [Test]
    public async Task C467_V30_ReceiptSaveFailureNeverRetypes()
    {
        await using var f = new LandDeliveryFixture(); await f.InitializeAsync(); await f.UseChildAsync("receipt");
        await f.RequestAsync(); await f.ReleaseExecutionAsync();
        await LandDeliveryFixture.UntilAsync(() => Task.FromResult(File.Exists(Path.Combine(f.Root, "receipt-before-save.barrier.json"))), "native prompt persisted before receipt save");
        await using (var db = f.CreateContext())
        {
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == f.TaskId && n.Kind == LandNotificationKind.Outcome);
            note.ConfirmedAt.ShouldBeNull();
            (await db.TranscriptEntries.AnyAsync(p => p.AgentSessionId == f.CallerId && p.Kind == "UserPrompt" && p.Text!.Contains(note.Id.ToString("N")))) .ShouldBeTrue();
        }
        await f.SnapshotAsync(); await f.KillChildAsync(); await f.UseChildAsync("none");
        var received = await f.ReceiptAsync(); await f.AssertOnePromptAsync(received);
    }
}

using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel("C467LandDelivery")]
[Category("OptIn")]
public class DispatchBaseWarningDeliveryE2ETests
{
    [Test]
    public async Task C540_CollapsedWarningsReachIdleCaller()
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(dispatch: true); await f.StartDispatchAsync();
        var intents = await f.WaitForDispatchIntentsAsync();
        await f.AssertDispatchReceiptsAsync(intents);
    }

    [Test]
    public async Task C540_CollapsedWarningsWaitForBusyCaller()
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(busy: true, dispatch: true); await f.StartDispatchAsync();
        var intents = await f.WaitForDispatchIntentsAsync();
        await f.TwoNotificationScansAsync();
        await using (var db = f.CreateContext())
        {
            var rows = await db.SessionQueuedMessages.Where(m => m.SourceTaskId == f.TaskId && m.SourceLandNotificationId != null).ToListAsync();
            rows.Count.ShouldBe(2); rows.ShouldAllBe(m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0);
            (await db.AgentTaskLandNotifications.Where(n => n.TaskId == f.TaskId).ToListAsync()).ShouldAllBe(n => n.ConfirmedAt == null);
            (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == f.CallerId && p.Kind == TranscriptKinds.UserPrompt
                && (p.Text!.Contains("[dispatch-base ") || p.Text.Contains("kept branch")))).ShouldBe(0);
        }
        await f.ReleaseBusyAsync(); await f.AssertDispatchReceiptsAsync(intents);
    }

    [Test]
    public Task C540_ClaimCrashRecoversCollapsedWarnings() => CrashAsync("dispatch-claim", "dispatch-warning-claim-committed");

    [Test]
    public Task C540_ProjectionCrashRecoversOriginalPairs() => CrashAsync("dispatch-projection", "dispatch-warning-before-commit");

    [Test]
    public Task C540_PreEnqueueCrashRecoversWarnings() => CrashAsync("pre-enqueue", "before-enqueue");

    [Test]
    public async Task C540_EnqueueFailureRetriesWarnings()
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(cut: "enqueue-errors", dispatch: true); await f.StartDispatchAsync();
        var intents = await f.WaitForDispatchIntentsAsync();
        await LandDeliveryFixture.UntilAsync(() => Task.FromResult(Directory.GetFiles(f.Root, "enqueue-failure-*.json").Length == 2), "two owned enqueue failures");
        await using (var db = f.CreateContext())
        {
            var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == f.TaskId).ToListAsync();
            notes.ShouldContain(n => n.EnqueueAttempts > 0 && n.LastErrorCode != null && n.LastErrorCode.Contains("IOException"));
        }
        await f.AssertDispatchReceiptsAsync(intents);
    }

    [Test]
    public Task C540_QueueInsertCrashReusesRows() => CrashAsync("queue", "queue-inserted", busy: true);

    [Test]
    public Task C540_PreTypingCrashRecoversWarnings() => CrashAsync("attempt", "queue-before-typing");

    [Test]
    public Task C540_PostPromptCrashDoesNotRetype() => CrashAsync("verdict", "queue-before-verdict");

    [Test]
    public Task C540_ReceiptCrashDoesNotRetype() => CrashAsync("receipt", "receipt-before-save");

    private static async Task CrashAsync(string cut, string boundary, bool busy = false)
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(busy: busy, dispatch: true); await f.UseChildAsync(cut); await f.StartDispatchAsync();
        await f.WaitForBoundaryAsync(boundary);
        var intents = await f.WaitForDispatchIntentsAsync();
        var originalQueue = new Dictionary<Guid, Guid>();
        await using (var db = f.CreateContext())
        {
            if (cut is "dispatch-claim" or "dispatch-projection")
            {
                intents.ShouldAllBe(i => i.MaterializedAt == null);
                (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
                (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId)).ShouldBe(0);
            }
            else if (cut == "pre-enqueue")
            {
                (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId)).ShouldBe(2);
                (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == f.TaskId && m.SourceLandNotificationId != null)).ShouldBe(0);
            }
            else if (cut == "queue")
            {
                var rows = await db.SessionQueuedMessages.Where(m => m.SourceTaskId == f.TaskId && m.SourceLandNotificationId != null).ToListAsync();
                rows.ShouldNotBeEmpty(); rows.ShouldAllBe(m => m.DeliveryAttempts == 0);
                foreach (var row in rows)
                {
                    originalQueue.Add(row.SourceLandNotificationId!.Value, row.Id);
                    (await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == row.SourceLandNotificationId)).QueueMessageId.ShouldBeNull();
                }
            }
            else if (cut is "receipt" or "verdict")
            {
                var prompts = await db.TranscriptEntries.Where(p => p.AgentSessionId == f.CallerId && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
                prompts.ShouldContain(p => intents.Any(i => Antiphon.Agents.Pty.PromptSubmissionMatch.IsCompleteIn(i.Body, p.Text ?? "")));
            }
            else if (cut == "attempt")
            {
                var rows = await db.SessionQueuedMessages.Where(m => m.SourceTaskId == f.TaskId && m.SourceLandNotificationId != null).ToListAsync();
                rows.ShouldContain(m => m.DeliveryAttempts == 1 && m.Status == QueuedMessageStatus.Sent);
                (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == f.CallerId && p.Kind == TranscriptKinds.UserPrompt && p.Text!.Contains("[dispatch-base "))).ShouldBe(0);
            }
        }
        await f.SnapshotAsync(); await f.KillChildAsync();
        if (cut == "dispatch-claim") await f.MoveDispatchObservationsAsync();
        await f.UseChildAsync("none");
        if (busy) await f.ReleaseBusyAsync();
        await f.AssertDispatchReceiptsAsync(intents);
        await using var final = f.CreateContext();
        foreach (var (noteId, queueId) in originalQueue)
        {
            (await final.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == noteId)).Id.ShouldBe(queueId);
            (await final.AgentTaskLandNotifications.SingleAsync(n => n.Id == noteId)).QueueMessageId.ShouldBe(queueId);
        }
    }
}

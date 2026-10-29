using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
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
    public async Task C508_MismatchWarningReachesCaller()
    {
        // Vanished trunk: the mismatch intent and default-unresolved each reach the idle caller once.
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(dispatch: true); await f.StartMismatchDispatchAsync();
        var intents = await f.WaitForMismatchIntentAsync();
        await f.AssertDispatchReceiptsAsync(intents);
    }

    [Test]
    public async Task C508_MismatchWarningWaitsForCaller()
    {
        // The same vanished default, with the caller busy: both rows stay pending until release.
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(busy: true, dispatch: true); await f.StartMismatchDispatchAsync();
        var intents = await f.WaitForMismatchIntentAsync();
        await f.TwoNotificationScansAsync();
        await using (var db = f.CreateContext())
        {
            var rows = await db.SessionQueuedMessages.Where(m => m.SourceTaskId == f.TaskId && m.SourceLandNotificationId != null).ToListAsync();
            rows.Count.ShouldBe(2); rows.ShouldAllBe(m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0);
            var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == f.TaskId).ToListAsync();
            notes.Count.ShouldBe(2); notes.ShouldAllBe(n => n.ConfirmedAt == null);
            (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == f.CallerId && p.Kind == TranscriptKinds.UserPrompt
                && p.Text!.Contains("[dispatch-base "))).ShouldBe(0);
        }
        Directory.GetFiles(Path.Combine(f.Root, "native"), "updates.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines).Count(line => line.Contains("user_message_chunk")
                && line.Contains("[dispatch-base ")).ShouldBe(0);
        await f.ReleaseBusyAsync(); await f.AssertDispatchReceiptsAsync(intents);
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
        Directory.GetFiles(Path.Combine(f.Root, "native"), "updates.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines).Count(line => line.Contains("user_message_chunk")
                && (line.Contains("[dispatch-base ") || line.Contains("kept branch"))).ShouldBe(0);
        await f.ReleaseBusyAsync(); await f.AssertDispatchReceiptsAsync(intents);
    }

    [Test]
    public Task C540_ClaimCrashRecoversCollapsedWarnings() => CrashAsync("dispatch-claim", "dispatch-warning-claim-committed");

    [Test]
    public Task C540_ProjectionCrashRecoversOriginalPairs() => CrashAsync("dispatch-projection", "dispatch-warning-before-commit");

    [Test]
    public Task C540_PreEnqueueCrashRecoversWarnings() => CrashAsync("pre-enqueue", "before-enqueue");

    // CARD-1359: dispatch-side progress (claim rows, each materialization) keeps flowing while
    // protocol-git is silent for longer than the 60 s quiet window. The pre-enqueue wait must renew
    // on that progress instead of timing out behind a slow launch path.
    [Test]
    public Task C540_PreEnqueueWaitSurvivesSlowMaterialization() => CrashAsync("pre-enqueue", "before-enqueue", slowMaterializeSeconds: 40);

    [Test]
    public async Task C540_EnqueueFailureRetriesWarnings()
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(cut: "enqueue-errors", dispatch: true); await f.StartDispatchAsync();
        var intents = await f.WaitForDispatchIntentsAsync();
        try
        {
            await LandDeliveryFixture.UntilAsync(() => Task.FromResult(Directory.GetFiles(f.Root, "enqueue-failure-*.json").Length == 2), "two owned enqueue failures");
        }
        catch (TimeoutException ex)
        {
            throw await f.DispatchTimeoutAsync(ex);
        }
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

    private static async Task CrashAsync(string cut, string boundary, bool busy = false, int slowMaterializeSeconds = 0)
    {
        await using var f = new LandDeliveryFixture();
        await f.InitializeAsync(busy: busy, dispatch: true); await f.UseChildAsync(cut);
        if (slowMaterializeSeconds > 0)
            await f.WriteSlowLaunchAsync(new Dictionary<string, int> { ["dispatch-warning-before-materialize"] = slowMaterializeSeconds });
        await f.StartDispatchAsync();
        if (cut == "queue")
        {
            // A missing key also disables the production keyed-insert callback. Observe the
            // stored key independently so PC-21 reaches an assertion instead of losing its cut.
            // CARD-1359: dispatch-aware wait (protocol-git plus dispatch-side markers), same window.
            await f.UntilDispatchAsync(async () =>
            {
                await using var db = f.CreateContext();
                (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == f.TaskId
                    && m.Body.StartsWith("[dispatch-base ") && m.SourceLandNotificationId == null))
                    .ShouldBe(0, "every dispatch warning must retain its notification key before the insert barrier");
                return File.Exists(Path.Combine(f.Root, boundary + ".barrier.json"));
            }, boundary);
        }
        else await f.WaitForDispatchBoundaryAsync(boundary); // CARD-1359: renews on dispatch-side markers too
        if (slowMaterializeSeconds > 0) f.ClearSlowLaunch(); // the restarted "none" child must never see the seam
        var intents = await f.WaitForDispatchIntentsAsync();
        if (slowMaterializeSeconds > 0) await f.AssertSlowMaterializationPremiseAsync(boundary, intents, slowMaterializeSeconds);
        var originalQueue = new Dictionary<Guid, Guid>();
        var interruptedAttempts = new Dictionary<Guid, int>();
        await using (var db = f.CreateContext())
        {
            if (cut is "dispatch-claim" or "dispatch-projection")
            {
                // Projection has not committed. A bare Warning count also fails when the claim
                // committed some other warning (quota, capacity consume) and left the intents open.
                intents.ShouldAllBe(i => i.MaterializedAt == null);
                var warnings = await db.AgentTaskEvents.AsNoTracking()
                    .Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning)
                    .Select(e => new { e.Id, e.Detail })
                    .ToListAsync();
                var observed = DispatchWaitDiagnostic.FormatWarnings(
                    warnings.Select(w => new DispatchWaitDiagnostic.WarningRow(w.Id, w.Detail)));
                Console.WriteLine("C540 crash-cut warnings at " + boundary + ": " + observed);
                var intentIds = intents.Select(i => i.Id).ToHashSet();
                warnings.Where(w => DispatchWaitDiagnostic.IsUnmaterializedProjection(w.Id, w.Detail, intentIds))
                    .ShouldBeEmpty("dispatch-base projection leaked at " + boundary + "; warnings=" + observed);
                (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId))
                    .ShouldBe(0, "dispatch-base notification leaked at " + boundary + "; warnings=" + observed);
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
                prompts.ShouldContain(p => intents.Any(i => PromptSubmissionMatch.IsCompleteIn(i.Body, p.Text ?? "")));
                if (cut == "verdict")
                {
                    // The crashed attempt's own row: prompt already confirmed, verdict never saved.
                    var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceTaskId == f.TaskId
                        && m.SourceLandNotificationId != null && m.Status == QueuedMessageStatus.Sent && m.DeliveryVerdict == null).ToListAsync();
                    rows.ShouldNotBeEmpty(); rows.ShouldAllBe(m => m.DeliveryAttempts > 0);
                    foreach (var row in rows) interruptedAttempts.Add(row.Id, row.DeliveryAttempts);
                }
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
        if (cut is "attempt" or "verdict") await f.WaitForInterruptedDispatchEligibilityAsync();
        await f.UseChildAsync("none");
        if (busy) await f.ReleaseBusyAsync();
        // Review F2: receipt confirmation alone does not exercise queue recovery. Only after the
        // interrupted row is eligible must transcript evidence settle that same row, uncharged.
        if (interruptedAttempts.Count > 0) await f.WaitForLateConfirmedInterruptedRowsAsync(interruptedAttempts);
        await f.AssertDispatchReceiptsAsync(intents);
        await using var final = f.CreateContext();
        foreach (var (rowId, attempts) in interruptedAttempts)
        {
            var row = await final.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == rowId);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed); row.DeliveryAttempts.ShouldBe(attempts);
        }
        foreach (var (noteId, queueId) in originalQueue)
        {
            (await final.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == noteId)).Id.ShouldBe(queueId);
            (await final.AgentTaskLandNotifications.SingleAsync(n => n.Id == noteId)).QueueMessageId.ShouldBe(queueId);
        }
    }
}

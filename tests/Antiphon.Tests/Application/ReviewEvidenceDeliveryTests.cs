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

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewEvidenceDeliveryTests
{
    private const string Warning = "review-evidence-warning=review_evidence_not_standalone";
    private static readonly string[] HeaderBits = [Warning, "fenced, quoted or indented", "bare lines before the next-stage block",
        "verification=Final", "scope=Unknown", "final-review=none", "next=land"];

    private static string Presented(string report, string kind)
    {
        var start = report.IndexOf(ReviewEvidence.Heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = report.IndexOf("\n\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        var block = report[start..end];
        var rendered = kind == "fence" ? "```\n" + block + "\n```"
            : string.Join("\n", block.Split('\n').Select(line => "> " + line));
        return report[..start] + rendered + report[end..];
    }

    private static Task<(Guid TaskId, string Report)> SettleWarningAsync(C544DeliveryRig rig, string kind = "fence",
        int padding = 0) => rig.SettleReviewAsync(padding: padding,
            transformReport: (_, report) => Presented(report, kind));

    private static async Task AssertWarningReceiptAsync(C544DeliveryRig rig, Guid taskId, string row,
        string? kind = null, bool? spill = null)
    {
        await VerificationRoundDeliveryTests.AssertReceivedOnceAsync(rig, taskId, row, HeaderBits, kind, spill);
        var note = (await rig.NotificationAsync(taskId))!;
        var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!;
        var delivery = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson)!;
        snapshot.NoteHeader.ShouldNotContain("review-evidence=", row);
        snapshot.NoteHeader.ShouldNotContain("reviewed-sha=", row);
        delivery.LogicalNote.ShouldNotContain("review-evidence=", row);
        rig.Caller.SubmittedBodies.ShouldContain(body => PromptSubmissionMatch.IsCompleteIn(delivery.WireText, body), row);
        if (delivery.SpillPath is { } path)
            (await File.ReadAllTextAsync(path)).ShouldContain(Warning, row);
    }

    [Test]
    public async Task C807_WarningReceipt()
    {
        foreach (var busy in new[] { false, true })
        foreach (var presentation in new[] { "fence", "quote" })
        foreach (var rendering in new[] { "raw-inline", "distilled-inline", "polled-inline", "raw-spill" })
        {
            var row = $"busy={busy} {presentation} {rendering}";
            var spill = rendering == "raw-spill";
            var distill = rendering == "distilled-inline";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill, distill);
            var (taskId, _) = await SettleWarningAsync(rig, presentation);
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row);
            snapshot.NoteHeader.ShouldContain(Warning, row);
            rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": no enqueue receipt");
            if (distill)
            {
                rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue(row);
                (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary,
                    CancellationToken.None)).ShouldBeNull(row);
                var queued = await rig.RowAsync(taskId);
                queued.NoteHeader.ShouldContain(Warning, row);
                queued.Body.ShouldContain(Warning, row);
                queued.Body.ShouldContain(C544DeliveryRig.Summary, row);
            }
            if (rendering == "polled-inline")
            {
                await using var scope = rig.World.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                    .GetAsync(taskId, CancellationToken.None, pollingSessionId: rig.World.CallerSessionId);
                await using var db = rig.World.CreateContext();
                (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.NoteShrunk))
                    .ShouldBeTrue(row);
            }
            await rig.DeliverAsync(row);
            await rig.ScanAsync();
            await AssertWarningReceiptAsync(rig, taskId, row, distill ? "distilled" : null, spill);
            if (rendering == "polled-inline")
            {
                var delivery = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)!;
                delivery.LogicalNote.ShouldContain("Report withheld", row);
                delivery.LogicalNote.ShouldContain(Warning, row);
            }
        }
    }

    [Test] public Task C807_RecoveryBeforeSettlementCommit() => RecoverAsync("obligation-insert");
    [Test] public Task C807_RecoveryAfterSettlementCommit() => RecoverAsync("settled-committed");
    [Test] public Task C807_RecoveryBeforeEnqueue() => RecoverAsync("note-insert");
    [Test] public Task C807_RecoveryAfterEnqueue() => RecoverAsync("note-committed");
    [Test] public Task C807_RecoveryLostWakeup() => RecoverAsync("wakeup-dropped");
    [Test] public Task C807_RecoveryBeforeRenderingCommit() => RecoverAsync("spill-written");
    [Test] public Task C807_RecoveryAfterRenderingCommit() => RecoverAsync("render-committed");
    [Test] public Task C807_RecoveryAfterAttemptCommit() => RecoverAsync("attempt-committed");
    [Test] public Task C807_RecoveryBeforeReceiptCommit() => RecoverAsync("prompt-accepted");

    private static async Task RecoverAsync(string cut)
    {
        foreach (var busy in new[] { false, true })
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"{cut} busy={busy} distilledSpill={distilledSpill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill, distill: distilledSpill);
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else rig.Fault.Cut = cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted"
                ? null : cut;
            var (taskId, _) = await SettleWarningAsync(rig);
            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, row);
                (await rig.NotificationAsync(taskId)).ShouldBeNull(row);
                (await rig.World.TaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched, row);
                await using (var db = rig.World.CreateContext())
                {
                    (await db.StageOutcomes.CountAsync(o => o.StageTaskId == taskId)).ShouldBe(0, row);
                    (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Warning)).ShouldBe(0, row);
                    (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Completed)).ShouldBe(0, row);
                }
                await rig.RestartAsync();
                var session = (await rig.World.TaskAsync(taskId)).AgentSessionId!.Value;
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            }
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row);
            snapshot.NoteHeader.ShouldContain(Warning, row);
            var oldQueue = await rig.RowsAsync(taskId);
            if (cut is "settled-committed" or "note-insert") oldQueue.ShouldBeEmpty(row);
            if (cut == "note-committed") oldQueue.ShouldHaveSingleItem(row);
            if (distilledSpill && cut is not ("settled-committed" or "note-insert"))
            {
                if (rig.DistillQueue.TryDequeue(out var request))
                    (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary,
                        CancellationToken.None)).ShouldBeNull(row);
            }
            if (cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")
            {
                rig.Fault.TaskId = taskId;
                rig.Fault.Cut = cut;
                try
                {
                    if (busy) await rig.EndCallerTurnAsync();
                    else await rig.FlushAsync();
                    if (cut == "prompt-accepted") await rig.ScanAsync();
                }
                catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
                rig.Fault.Throws.ShouldBe(1, row);
            }
            var atCut = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson);
            if (cut == "render-committed") atCut.ShouldNotBeNull(row);
            if (cut == "spill-written") atCut.ShouldBeNull(row);
            if (cut is "render-committed" or "spill-written") rig.Caller.SubmittedBodies.ShouldBeEmpty(row);
            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            if (rig.Busy) await rig.EndCallerTurnAsync();
            await rig.FlushAsync();
            await rig.ScanAsync();
            await AssertWarningReceiptAsync(rig, taskId, row, spill: distilledSpill ? null : false);
            var recovered = (await rig.NotificationAsync(taskId))!;
            recovered.Id.ShouldBe(note.Id, row);
            recovered.SourceEventId.ShouldBe(note.SourceEventId, row);
            var recoveredSnapshot = TaskCompletionNotification.TryReadSnapshot(recovered.CompletionSnapshotJson)!;
            recoveredSnapshot.StageOutcomeId.ShouldBe(snapshot.StageOutcomeId, row);
            recovered.ContentDigest.ShouldBe(note.ContentDigest, row);
            if (oldQueue.Count == 1)
                (await rig.RowAsync(taskId)).Id.ShouldBe(oldQueue[0].Id, row);
            if (atCut is not null)
                TaskCompletionNotification.TryReadDelivery(recovered.CompletionDeliveryJson)!.WireText
                    .ShouldBe(atCut.WireText, row);
        }
    }

    [Test]
    public async Task C807_SnapshotWarningSurvivesResultRewrite()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        rig.Fault.Cut = "settled-committed";
        var (taskId, _) = await SettleWarningAsync(rig);
        rig.Fault.Throws.ShouldBe(1);
        var original = (await rig.NotificationAsync(taskId))!;
        var snapshot = TaskCompletionNotification.TryReadSnapshot(original.CompletionSnapshotJson)!;
        await using (var db = rig.World.CreateContext())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Result,
                "FOREIGN REWRITE\n--- review evidence ---\nsubjectTaskId: " + rig.World.Owner.Id
                + "\nreviewedSourceSha: " + rig.World.OwnerSha));
        await rig.RestartAsync();
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        var queued = await rig.RowAsync(taskId);
        queued.NoteHeader.ShouldBe(snapshot.NoteHeader);
        queued.Body.ShouldContain(Warning);
        queued.Body.ShouldNotContain("FOREIGN REWRITE");
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertWarningReceiptAsync(rig, taskId, "snapshot", "raw", false);
        var saved = (await rig.NotificationAsync(taskId))!;
        saved.CompletionSnapshotJson.ShouldBe(original.CompletionSnapshotJson);
        saved.ContentDigest.ShouldBe(original.ContentDigest);
    }

    [Test]
    public async Task C807_WarningReplayRejectsLateDistillation()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, distill: true);
        var (taskId, _) = await SettleWarningAsync(rig);
        var note = (await rig.NotificationAsync(taskId))!;
        var held = await rig.RowAsync(taskId);
        rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue();
        rig.World.Clock.Advance(held.HoldUntil!.Value - rig.World.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5));
        rig.Fault.TaskId = taskId;
        rig.Fault.Cut = "attempt-committed";
        try { await rig.FlushAsync(); }
        catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
        rig.Fault.Throws.ShouldBe(1);
        var frozen = (await rig.NotificationAsync(taskId))!.CompletionDeliveryJson.ShouldNotBeNull();
        var before = await rig.RowAsync(taskId);
        var rejected = await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary,
            CancellationToken.None);
        rejected.ShouldBe("delivery-claimed");
        (await rig.RowAsync(taskId)).Body.ShouldBe(before.Body);
        (await rig.NotificationAsync(taskId))!.CompletionDeliveryJson.ShouldBe(frozen);
        await rig.RestartAsync();
        await rig.ScanAsync();
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertWarningReceiptAsync(rig, taskId, "late", "raw", false);
    }

    [Test]
    public async Task C807_DistillationDeadlineIsNotRenewed()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, distill: true);
        rig.Fault.Cut = "settled-committed";
        var (taskId, _) = await SettleWarningAsync(rig);
        var snapshot = TaskCompletionNotification.TryReadSnapshot((await rig.NotificationAsync(taskId))!.CompletionSnapshotJson)!;
        var deadline = snapshot.DistillDeadlineAt.ShouldNotBeNull();
        await rig.RestartAsync();
        rig.World.Clock.Advance(TimeSpan.FromSeconds(30));
        await rig.ScanAsync();
        var row = await rig.RowAsync(taskId);
        (row.HoldUntil!.Value - deadline).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(1));
        rig.DistillQueue.TryDequeue(out _).ShouldBeFalse();
        await rig.FlushAsync();
        rig.Caller.SubmittedBodies.ShouldBeEmpty();
        rig.World.Clock.Advance(deadline - rig.World.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5));
        await rig.FlushAsync();
        await rig.ScanAsync();
        await AssertWarningReceiptAsync(rig, taskId, "deadline", "raw", false);
    }

    [Test]
    public async Task C807_HeaderOnlyPromptIsNotReceipt()
    {
        foreach (var form in new[] { "header", "prefix", "id" })
        {
            await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
            var (taskId, _) = await SettleWarningAsync(rig);
            var note = (await rig.NotificationAsync(taskId))!;
            rig.SubmitTransform = wire => form switch
            {
                "header" => wire.ReplaceLineEndings("\n").Split('\n')[0],
                "prefix" => wire[..(wire.Length / 2)],
                _ => $"notification {note.Id:N} task {taskId:N}",
            };
            await rig.FlushAsync();
            await rig.ScanAsync();
            var refused = (await rig.NotificationAsync(taskId))!;
            refused.State.ShouldNotBe(LandNotificationState.Confirmed, form);
            refused.ConfirmingPromptSequence.ShouldBeNull(form);
            if (form == "id")
            {
                rig.SubmitTransform = null;
                await rig.RestartAsync();
                rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
                await rig.ScanAsync();
                await rig.FlushAsync();
                await rig.ScanAsync();
                await AssertWarningReceiptAsync(rig, taskId, form, "raw", false);
            }
        }
    }

    [Test]
    public async Task C807_SpillWarningRequiresMatchingFile()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false, spill: true);
        var (taskId, _) = await SettleWarningAsync(rig);
        await rig.DeliverAsync("spill");
        var delivery = TaskCompletionNotification.TryReadDelivery((await rig.NotificationAsync(taskId))!.CompletionDeliveryJson)!;
        var path = delivery.SpillPath.ShouldNotBeNull();
        var bytes = await File.ReadAllBytesAsync(path);
        await File.WriteAllTextAsync(path, "tampered");
        await rig.ScanAsync();
        var refused = (await rig.NotificationAsync(taskId))!;
        refused.State.ShouldNotBe(LandNotificationState.Confirmed);
        refused.ConfirmingPromptSequence.ShouldBeNull();
        refused.LastErrorCode.ShouldBe("completion_pointer_content_mismatch");
        await File.WriteAllBytesAsync(path, bytes);
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        await rig.ScanAsync();
        await AssertWarningReceiptAsync(rig, taskId, "spill-restored", "raw", true);
    }

    [Test]
    public async Task C807_ReceiptQueryRequiresDestinationKindAndFloor()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        var (taskId, _) = await SettleWarningAsync(rig);
        await rig.DeliverAsync("query");
        await rig.ScanAsync();
        await AssertWarningReceiptAsync(rig, taskId, "query", "raw", false);
        var note = (await rig.NotificationAsync(taskId))!;
        var delivery = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson)!;
        var prompts = await rig.CallerPromptsAsync();
        var complete = prompts.Single(p => p.Sequence == note.ConfirmingPromptSequence);
        complete.Kind.ShouldBe(TranscriptKinds.UserPrompt);
        complete.Sequence.ShouldBeGreaterThan((await rig.RowAsync(taskId)).LastDeliveryBaselineSequence ?? 0);
        PromptSubmissionMatch.IsCompleteIn(delivery.WireText, complete.Text!).ShouldBeTrue();
    }

    [Test]
    public async Task C807_LegacyWarningReceipt()
    {
        foreach (var busy in new[] { false, true })
        foreach (var form in new[] { "fence", "quote" })
        {
            var row = $"busy={busy} {form}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy);
            var created = await rig.World.CreateTaskAsync(rig.World.FinalReview());
            await using (var db = rig.World.CreateContext())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
                task.VerificationProfileVersion = null;
                task.VerificationRound = null;
                await db.SaveChangesAsync();
            }
            var session = await rig.World.DispatchAsync(created.Id);
            var report = Presented(C544World.ReviewReport(created.Id, rig.World.Owner.Id, rig.World.OwnerSha,
                "Full", found: false), form);
            await rig.World.SeedTurnAsync(session, created.Id, report);
            await rig.World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            (await rig.NotificationAsync(created.Id)).ShouldBeNull(row + ": legacy has no completion snapshot");
            var queued = await rig.RowAsync(created.Id);
            queued.NoteHeader.ShouldContain(Warning, row);
            await rig.DeliverAsync(row);
            var prompts = await rig.CallerPromptsAsync();
            var carrying = prompts.Where(p => p.Text is not null && PromptSubmissionMatch.IsCompleteIn(queued.Body, p.Text)).ToList();
            carrying.ShouldHaveSingleItem(row);
            carrying[0].Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, row);
            carrying[0].Text.ShouldContain(Warning, row);
        }
    }
}

using Antiphon.Server.Application.Dtos;
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
/// CARD-0544 V-4 / DL-1..DL-3, R-3/R-9/R-10/R-11. Producer-to-recipient delivery of the D-9 Completion
/// obligation (and the landing refusal and brief legs) through the real reply settlement, notification
/// scanner, reconcile service and session queue, into a controlled caller terminal. Receipt is only
/// the complete caller UserPrompt above the attempt floor; a busy caller receives zero writes before
/// its TurnEnd; recovery runs on recreated providers without editing task/notification/queue rows.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerificationRoundDeliveryTests
{
    private static readonly string[] FinalHeaderBits = ["verification=Final", "scope=Full", "final-review=none", "next=land"];

    [Test]
    public async Task C544_CompletionReceipt()
    {
        foreach (var busy in new[] { false, true })
        foreach (var spill in new[] { false, true })
        foreach (var distill in new[] { false, true })
        {
            var row = $"busy={busy} spill={spill} distilled={distill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill, distill);
            var (taskId, report) = await rig.SettleReviewAsync();
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row + ": obligation");
            var rows = await rig.RowsAsync(taskId);
            rows.Count.ShouldBe(1, row + ": one keyed row");
            rows[0].SourceLandNotificationId.ShouldBe(note.Id, row);
            rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": nothing typed at enqueue (WhenIdle)");
            if (distill)
            {
                rows[0].HoldUntil.ShouldNotBeNull(row + ": held for distillation");
                await rig.FlushAsync();
                rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": held row not typed");
                rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue(row + ": one distiller admission");
                (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None))
                    .ShouldBeNull(row + ": distillation applied");
            }
            await rig.DeliverAsync(row);
            await rig.ScanAsync();
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: distill ? "distilled" : "raw", expectSpill: spill);
        }
    }

    [Test]
    public async Task C544_CompletionRecovery()
    {
        var cuts = new[]
        {
            "obligation-insert", "settled-committed", "note-insert", "note-committed", "wakeup-dropped",
            "render-committed", "spill-written", "attempt-committed", "prompt-accepted",
        };
        foreach (var cut in cuts)
        foreach (var busy in new[] { false, true })
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"{cut} busy={busy} {(distilledSpill ? "distilled-spill" : "raw-inline")}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill, distill: distilledSpill);
            Guid taskId;
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else rig.Fault.Cut = cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted" ? null : cut;
            (taskId, _) = await rig.SettleReviewAsync();

            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, row);
                (await rig.NotificationAsync(taskId)).ShouldBeNull(row + ": no obligation without its settlement");
                (await rig.World.TaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched, row + ": settlement rolled back");
                await rig.RestartAsync();
                var session = (await rig.World.TaskAsync(taskId)).AgentSessionId!.Value;
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
            }

            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row + ": committed obligation");
            if (distilledSpill && cut is not ("settled-committed" or "note-insert"))
            {
                // Distiller admission is part of the immediate path only; recovery never commissions another.
                if (rig.DistillQueue.TryDequeue(out var request))
                    (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest, C544DeliveryRig.Summary, CancellationToken.None))
                        .ShouldBeNull(row + ": immediate distillation");
            }

            if (cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")
            {
                rig.Fault.TaskId = taskId;
                rig.Fault.Cut = cut;
                if (busy) await rig.EndCallerTurnAsync().ContinueWith(_ => { });
                else await Should.ThrowAsync<IOException>(rig.FlushAsync).ContinueWith(_ => { });
                if (cut == "prompt-accepted") await rig.ScanAsync();
                rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
            }

            // Unconfirmed until the complete prompt exists: never confirmed at enqueue.
            var promptsNow = await rig.CallerPromptsAsync();
            var current = (await rig.NotificationAsync(taskId))!;
            var delivery = TaskCompletionNotification.TryReadDelivery(current.CompletionDeliveryJson);
            var completeExists = delivery is not null && promptsNow.Any(p => PromptSubmissionMatch.IsCompleteIn(delivery.WireText, p.Text!));
            if (!completeExists)
            {
                current.State.ShouldNotBe(LandNotificationState.Confirmed, row + ": not confirmed without the complete prompt");
                current.ConfirmingPromptSequence.ShouldBeNull(row);
            }
            if (cut is "render-committed" or "spill-written")
                rig.Caller.SubmittedBodies.ShouldBeEmpty(row + ": nothing typed at the cut");

            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            if (rig.Busy) await rig.EndCallerTurnAsync();
            await rig.FlushAsync();
            await rig.ScanAsync();
            await AssertReceivedOnceAsync(rig, taskId, row, FinalHeaderBits, expectKind: null, expectSpill: distilledSpill ? null : false);
            (await rig.NotificationAsync(taskId))!.Id.ShouldBe(note.Id, row + ": same notification identity");
        }
    }

    // ---- shared assertions ------------------------------------------------------------------------

    internal static async Task AssertReceivedOnceAsync(C544DeliveryRig rig, Guid taskId, string row, string[] headerBits,
        string? expectKind, bool? expectSpill)
    {
        var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
        var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row + ": snapshot");
        var delivery = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson).ShouldNotBeNull(row + ": frozen rendering");
        var rows = await rig.RowsAsync(taskId);
        rows.Count.ShouldBe(1, row + ": exactly one queue row");
        var queued = rows[0];
        queued.SourceLandNotificationId.ShouldBe(note.Id, row);
        queued.AgentSessionId.ShouldBe(rig.World.CallerSessionId, row + ": snapshotted destination");
        queued.ConversationKey.ShouldBe($"task:{snapshot.RootTaskId:N}", row);
        queued.ContentDigest.ShouldBe(snapshot.NoteDigest, row);
        delivery.MemberQueueIds.ShouldContain(queued.Id, row);

        var prompts = await rig.CallerPromptsAsync();
        var carrying = prompts.Where(p => p.Text is not null && PromptSubmissionMatch.IsConfirmedBy(delivery.WireText, p.Text)).ToList();
        carrying.Count.ShouldBe(1, row + ": exactly one caller UserPrompt carries the wire text");
        PromptSubmissionMatch.IsCompleteIn(delivery.WireText, carrying[0].Text!).ShouldBeTrue(row + ": complete prompt");
        carrying[0].Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0, row + ": above the attempt floor");
        TaskCompletionNotification.Sha256(delivery.WireText).ShouldBe(delivery.WireSha256, row);

        note.State.ShouldBe(LandNotificationState.Confirmed, $"{row}: {note.LastErrorCode}");
        note.ConfirmingPromptSequence.ShouldBe(carrying[0].Sequence, row);
        note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(snapshot.RawResult), row + ": raw digest identity");

        var logical = delivery.LogicalNote;
        foreach (var bit in headerBits)
        {
            snapshot.NoteHeader.ShouldContain(bit, Case.Sensitive, row + ": snapshot header " + bit);
            logical.ShouldContain(bit, Case.Sensitive, row + ": rendering keeps " + bit);
        }
        logical.ShouldStartWith(snapshot.NoteHeader, Case.Sensitive, row + ": header first");
        if (expectKind is not null)
            delivery.RenderingKind.Split('+')[0].ShouldBe(expectKind, row + ": rendering kind");
        if (expectKind == "distilled")
            logical.ShouldContain(C544DeliveryRig.Summary, Case.Sensitive, row);
        if (expectSpill is { } spill)
            (delivery.SpillPath is not null).ShouldBe(spill, row + ": spill");
        if (delivery.SpillPath is { } path)
        {
            (await AgentTaskLandNotificationService.FileHasSha256Async(path, delivery.SpillSha256, CancellationToken.None))
                .ShouldBeTrue(row + ": pointer content hash");
            var content = await File.ReadAllTextAsync(path);
            foreach (var bit in headerBits)
                content.ShouldContain(bit, Case.Sensitive, row + ": spilled content keeps " + bit);
        }
    }
}

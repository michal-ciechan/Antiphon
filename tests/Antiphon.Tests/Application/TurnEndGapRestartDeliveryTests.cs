using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Microsoft.EntityFrameworkCore;
using Antiphon.Agents.Pty;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow")]
[NotInParallel("MessageQueue"), ParallelLimiter<ProcessSpawnLimit>]
public sealed class TurnEndGapRestartDeliveryTests
{
    // T37 / V-20 / R-5 -> PC-84 suppresses Sync's recovered-boundary wakeup.
    // Red oracle: five complete recipient prompts and Confirmed notifications,
    // after the finite captured-gap / keepalive / queue-timer schedule.
    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Restart_delivers_five_gap_held_completions_once(bool cache)
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync(cache);
        await h.ProduceFiveAsync();
        await h.AssertHeldAsync();
        var notes = await h.NotesAsync();
        var rows = await h.RowsAsync();
        await h.RestartAsync();
        await h.AssertHeldAsync();
        await h.StartCompletionWorkerAsync();
        await h.AssertHeldAsync();
        await h.StartGapAsync();
        h.ReleaseGapWithEnd();
        await h.JoinDeliveryAsync(h.PumpDrained);
        await h.JoinOwnedWorkAsync();
        await h.AssertReceiptsAsync(notes, rows);
        await h.RestartAsync();
        await h.AssertReceiptsAsync(notes, rows);
    }

    // T38 -> PC-85: removing admission leaves zero receipts at the joined due pass.
    [Test]
    public async Task C1205_Watchdog_delivers_while_event_stream_remains_healthy()
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceFiveAsync();
        var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        await h.StartCompletionWorkerAsync(); await h.StartStreamAsync();
        await h.ProbeAsync(); h.RecoveryOutcomes.Last().ShouldBe("CheckedWorking");
        h.Flushes.ClearAudit();
        h.MissEnd();
        h.Clock.Advance(TimeSpan.FromSeconds(119));
        await h.KeepaliveAsync(); await h.ProbeAsync();
        h.Flushes.Accepted.ShouldBeEmpty(); await h.AssertHeldAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await h.ProbeAsync();
        h.RecoveryOutcomes.Last().ShouldBe("RecoveredIdle");
        h.Flushes.Accepted.ShouldContain(h.Id);
        h.Keepalives.ShouldBeGreaterThanOrEqualTo(2);
        await h.JoinOwnedWorkAsync(); await h.AssertReceiptsAsync(notes, rows);
    }

    // T39 -> PC-86: a lost volatile wakeup must be recovered by the real boot scanner.
    [Test]
    public async Task C1205_Restart_after_transcript_commit_recovers_lost_wakeup()
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceFiveAsync(); var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        await h.DrainWakeupsAsync(); h.MissEnd();
        h.Flushes.Hold = true;
        await h.Recovery.TickAsync(h.Token);
        await h.Flushes.Held.Task.WaitAsync(h.Token);
        (await h.WorkingAsync()).ShouldBeFalse(); (await h.EndCountAsync()).ShouldBe(1);
        h.Caller.SubmittedBodies.ShouldBeEmpty(); h.Flushes.Accepted.ShouldBeEmpty();
        var stopping = h.Recovery.StopAsync(CancellationToken.None);
        h.Flushes.DropHeld = true; h.Flushes.ReleaseHold.TrySetResult(); await stopping;
        h.Flushes.Accepted.ShouldBeEmpty();
        await h.RestartAsync();
        await h.StartCompletionWorkerAsync(); await h.JoinOwnedWorkAsync();
        h.Boundary.Count("completion-scan").ShouldBeGreaterThanOrEqualTo(5);
        await h.AssertReceiptsAsync(notes, rows);
    }

    // T40 -> PC-87/88: a committed receipt discharges the original note without retyping.
    [Test]
    public async Task C1205_Receipt_restart_confirms_without_retyping()
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceAsync(); var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        await h.MakeIdleAsync(); await h.DeliverExplicitlyAsync();
        h.Boundary.ThrowOnce = "receipt-before-save";
        await h.ReconcileAsync(); h.Boundary.Throws.ShouldBe(1);
        var interrupted = (await h.NotesAsync()).Single();
        interrupted.State.ShouldNotBe(LandNotificationState.Confirmed); interrupted.ConfirmingPromptSequence.ShouldBeNull();
        var delivery = TaskCompletionNotification.TryReadDelivery(interrupted.CompletionDeliveryJson).ShouldNotBeNull();
        var sequence = (await h.PromptsAsync()).Single(p => p.Text?.ReplaceLineEndings("\n") == delivery.WireText.ReplaceLineEndings("\n")).Sequence;
        h.Caller.SubmittedBodies.Count.ShouldBe(1);
        await h.RestartAsync(); await h.AssertReceiptsAsync(notes, rows);
        (await h.NotesAsync()).Single().ConfirmingPromptSequence.ShouldBe(sequence);
        await h.ReconcileAsync(2); h.Caller.SubmittedBodies.Count.ShouldBe(1);
        (await h.NotesAsync()).Single().ConfirmingPromptSequence.ShouldBe(sequence);
    }

    // T41 -> PC-89: busy authoritative activity protects all five durable obligations.
    [Test]
    public async Task C1205_Busy_tail_keeps_completion_obligations_pending()
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceFiveAsync(); var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        h.MissEnd(); await h.NewTurnAsync();
        await h.StartCompletionWorkerAsync();
        for (var pass = 0; pass < 3; pass++)
        {
            await h.ProbeAsync(); await h.DeliverExplicitlyAsync(); await h.AssertHeldAsync();
            h.RecoveryOutcomes.Last().ShouldBe("CheckedWorking");
            (await h.EndCountAsync()).ShouldBe(0);
            h.Clock.Advance(TimeSpan.FromSeconds(120));
        }
        await h.RestartAsync(); await h.StartCompletionWorkerAsync();
        await h.ProbeAsync(); await h.DeliverExplicitlyAsync(); await h.AssertHeldAsync();
        (await h.NotesAsync()).Select(n => n.Id).ShouldBe(notes.Select(n => n.Id));
        (await h.RowsAsync()).Select(r => r.Id).ShouldBe(rows.Select(r => r.Id));
        (await h.EndCountAsync()).ShouldBe(0);
    }

    // T42 -> PC-90: the primary keyed admission itself must publish its ordinary wakeup.
    [Test]
    public async Task C1205_Already_eligible_caller_receives_completion()
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync(busy: false);
        await h.StartCompletionWorkerAsync(minimumScans: 0);
        await h.StopCompletionWorkerAsync(); h.Flushes.ClearAudit();
        await h.ProduceAsync();
        h.Flushes.Accepted.ShouldContain(h.Id, "the producer wakeup precedes any boot recovery of this obligation");
        var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        await h.StartCompletionWorkerAsync(); await h.JoinOwnedWorkAsync();
        await h.AssertReceiptsAsync(notes, rows);
    }

    // T44 -> PC-96: Unknown cannot wake/attempt; a later checked idle probe delivers.
    [Test, Arguments("wrong-generation"), Arguments("pull-fault")]
    public async Task C1205_Unknown_probe_keeps_obligations_held(string uncertainty)
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceFiveAsync(); var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        await h.StartCompletionWorkerAsync(); h.Flushes.ClearAudit(); h.MissEnd();
        h.WrongGeneration = uncertainty == "wrong-generation"; h.FailPull = uncertainty == "pull-fault";
        await h.ProbeAsync(); h.RecoveryOutcomes.Last().ShouldBe("Unknown");
        h.Flushes.Accepted.ShouldBeEmpty(); await h.AssertHeldAsync(); (await h.EndCountAsync()).ShouldBe(0);
        await h.RestartAsync(); await h.StartCompletionWorkerAsync(); h.Flushes.ClearAudit();
        await h.ProbeAsync(); h.RecoveryOutcomes.Last().ShouldBe("Unknown");
        h.Flushes.Accepted.ShouldBeEmpty(); await h.AssertHeldAsync();
        (await h.NotesAsync()).Select(n => n.Id).ShouldBe(notes.Select(n => n.Id));
        (await h.RowsAsync()).Select(r => r.Id).ShouldBe(rows.Select(r => r.Id));
        h.WrongGeneration = false; h.FailPull = false;
        h.Clock.Advance(TimeSpan.FromSeconds(15)); await h.ProbeAsync();
        h.RecoveryOutcomes.Last().ShouldBe("RecoveredIdle"); h.Flushes.Accepted.ShouldContain(h.Id);
        await h.JoinOwnedWorkAsync(); await h.AssertReceiptsAsync(notes, rows);
    }

    // T46 -> PC-100: consume the recovered wakeup only after a newer turn commits.
    [Test]
    public async Task C1205_Flush_rechecks_a_new_turn_after_recovery()
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceFiveAsync(); await h.DrainWakeupsAsync();
        await using var reader = h.Flushes.ReadAllAsync(h.Token).GetAsyncEnumerator();
        var heldReader = reader.MoveNextAsync().AsTask();
        h.MissEnd(); await h.ProbeAsync();
        (await heldReader).ShouldBeTrue(); reader.Current.ShouldBe(h.Id);
        h.RecoveryOutcomes.Last().ShouldBe("RecoveredIdle");
        await h.NewTurnAsync(); (await h.WorkingAsync()).ShouldBeTrue();
        await h.JoinDeliveryAsync(h.Queue.FlushIfIdleAsync(reader.Current, h.Token));
        h.Flushes.Complete(reader.Current);
        await h.AssertHeldAsync(); h.Caller.SubmittedBodies.ShouldBeEmpty();
    }

    // T43 -> PC-91..95: each named cut is hit once before fresh-service recovery.
    [Test]
    [Arguments("obligation-insert", true), Arguments("obligation-insert", false)]
    [Arguments("settled-committed", true), Arguments("settled-committed", false)]
    [Arguments("note-insert", true), Arguments("note-insert", false)]
    [Arguments("note-committed", true), Arguments("note-committed", false)]
    [Arguments("render-committed", true), Arguments("render-committed", false)]
    [Arguments("attempt-committed", true), Arguments("attempt-committed", false)]
    [Arguments("wakeup-full", true), Arguments("wakeup-full", false)]
    [Arguments("receipt-before-save", true), Arguments("receipt-before-save", false)]
    public async Task C1205_Restart_recovers_each_delivery_handoff(string cut, bool busy)
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync(busy: busy);
        if (cut == "wakeup-full") { await FullChannelThenRestartAsync(h, busy); return; }
        var settlementCut = cut is "obligation-insert" or "settled-committed" or "note-insert" or "note-committed";
        if (settlementCut) h.Fault.Cut = cut;
        await h.ProduceAsync();
        if (busy)
        {
            h.Caller.SubmittedBodies.ShouldBeEmpty(); (await h.WorkingAsync()).ShouldBeTrue();
            if ((await h.RowsAsync()).Count != 0) await h.AssertHeldAsync();
        }
        if (settlementCut) h.Fault.Throws.ShouldBe(1);
        if (cut == "obligation-insert")
        {
            (await h.NotesAsync()).ShouldBeEmpty();
            var unsettled = await h.World.TaskAsync(h.Tasks.Single());
            unsettled.Status.ShouldBe(AgentTaskStatus.Dispatched);
            await using var rolledBack = h.World.CreateContext();
            (await rolledBack.AgentTaskEvents.CountAsync(e => e.AgentTaskId == unsettled.Id && e.Type == AgentTaskEventType.Completed, h.Token)).ShouldBe(0);
        }
        var before = await h.NotesAsync(); var queued = await h.RowsAsync();
        string? frozen = null;
        long? receiptSequence = null;
        if (!settlementCut)
        {
            await h.MakeIdleAsync();
            if (cut == "attempt-committed")
            {
                await h.StartStreamAsync();
                // This explicit flusher is independent of the pump: hold its
                // provider response until the real live ingestion commits.
                h.AfterSubmitted = h.WaitForSubmittedPromptAsync;
            }
            if (cut != "receipt-before-save") { h.Fault.TaskId = h.Tasks.Single(); h.Fault.Cut = cut; }
            try { await h.DeliverExplicitlyAsync(); }
            catch (IOException ex) when (ex.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
            if (cut == "receipt-before-save")
            {
                h.Boundary.ThrowOnce = cut;
                await h.ReconcileAsync(); h.Boundary.Throws.ShouldBe(1);
                var interrupted = (await h.NotesAsync()).Single();
                interrupted.State.ShouldNotBe(LandNotificationState.Confirmed); interrupted.ConfirmingPromptSequence.ShouldBeNull();
                var rendering = TaskCompletionNotification.TryReadDelivery(interrupted.CompletionDeliveryJson).ShouldNotBeNull();
                receiptSequence = (await h.PromptsAsync()).Single(p => p.Text?.ReplaceLineEndings("\n") == rendering.WireText.ReplaceLineEndings("\n")).Sequence;
            }
            else h.Fault.Throws.ShouldBe(1);
            frozen = (await h.NotesAsync()).Single().CompletionDeliveryJson.ShouldNotBeNull();
            if (cut == "render-committed")
            {
                h.Caller.SubmittedBodies.ShouldBeEmpty();
                var claimed = (await h.RowsAsync()).Single();
                claimed.Status.ShouldBe(QueuedMessageStatus.Sent); claimed.DeliveryAttempts.ShouldBe(1); claimed.DeliveryVerdict.ShouldBeNull();
            }
            else h.Caller.SubmittedBodies.Count.ShouldBe(1);
        }

        await h.RestartAsync(); h.AfterSubmitted = null;
        if (cut == "obligation-insert") await h.ReplaySettlementAsync();
        await h.AdvanceRetryAsync();
        await h.ScanNotificationsAsync();
        var recovered = (await h.NotesAsync()).ShouldHaveSingleItem();
        if (before.Count != 0)
        {
            recovered.Id.ShouldBe(before.Single().Id); recovered.SourceEventId.ShouldBe(before.Single().SourceEventId);
            recovered.ContentDigest.ShouldBe(before.Single().ContentDigest);
        }
        if (queued.Count != 0) (await h.RowsAsync()).Single().Id.ShouldBe(queued.Single().Id);
        else queued = await h.RowsAsync();
        before = await h.NotesAsync();
        if (cut == "render-committed")
        {
            // Existing InterruptedAttemptAge is 3+3+30 seconds in C544World.
            // The concrete stranded primitive owns this legacy recovery cadence.
            h.Clock.Advance(TimeSpan.FromSeconds(37));
            await h.JoinDeliveryAsync(h.Queue.FlushStrandedQueuesAsync(h.Token));
        }
        else if (h.Caller.SubmittedBodies.Count == 0)
        {
            if (busy) await h.MakeIdleAsync();
            await h.StartCompletionWorkerAsync(minimumScans: 0); await h.JoinOwnedWorkAsync();
        }
        await h.AssertReceiptsAsync(before, queued, expectedAttempts: cut == "render-committed" ? 2 : 1);
        if (frozen is not null) (await h.NotesAsync()).Single().CompletionDeliveryJson.ShouldBe(frozen);
        if (receiptSequence is not null) (await h.NotesAsync()).Single().ConfirmingPromptSequence.ShouldBe(receiptSequence);
        await using var completed = h.World.CreateContext();
        (await completed.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Tasks.Single() && e.Type == AgentTaskEventType.Completed, h.Token)).ShouldBe(1);
    }

    private static async Task FullChannelThenRestartAsync(TurnEndGapDeliveryHarness h, bool busy)
    {
        // The first obligation is the same-epoch RequestSweep control; the
        // second is a separate crash arm, so a boot sweep cannot mask that control.
        h.ConcurrentScopeBarriers = true;
        await h.StartCompletionWorkerAsync(minimumScans: 0);
        await h.SaturateWorkerAsync();
        h.Boundary.HoldAt = "completion-scan";
        var after = h.TimerSequence;
        await h.ProduceAsync();
        h.Boundary.Count("queue-committed-before-wakeup").ShouldBe(1);
        h.Flushes.Rejected.ShouldContain(h.Id);
        using var sweepObservation = CancellationTokenSource.CreateLinkedTokenSource(h.Token);
        var noSweep = h.NextBackstopAsync(after, sweepObservation.Token);
        try { await Task.WhenAny(h.Boundary.Held.Task, noSweep).WaitAsync(h.Token); }
        finally
        {
            sweepObservation.Cancel();
            try { await noSweep; } catch (OperationCanceledException) when (sweepObservation.IsCancellationRequested) { }
        }
        h.Boundary.Held.Task.IsCompleted.ShouldBeTrue("a full-channel rejection must request an actual same-epoch sweep");
        h.Boundary.Count("completion-scan").ShouldBe(1);
        h.Caller.SubmittedBodies.ShouldBeEmpty();
        if (busy) { await h.AssertHeldAsync(); await h.MakeIdleAsync(); }
        h.ReleaseWorkerCapacity(); h.Boundary.Release.TrySetResult();
        await h.JoinOwnedWorkAsync();
        await h.AssertReceiptsAsync(await h.NotesAsync(), await h.RowsAsync());
        await h.StopCompletionWorkerAsync(); h.ConcurrentScopeBarriers = false;
        h.Boundary.HoldAt = null;
        h.SaturateWithoutWorker();
        var rejected = h.Flushes.Rejected.Count(id => id == h.Id);
        await h.ProduceAsync();
        h.Flushes.Rejected.Count(id => id == h.Id).ShouldBe(rejected + 1);
        var notes = await h.NotesAsync(); var rows = await h.RowsAsync();
        rows.Count(r => r.Status == QueuedMessageStatus.Pending && r.DeliveryAttempts == 0).ShouldBe(1);
        h.Caller.SubmittedBodies.Count.ShouldBe(1);
        await h.RestartAsync();
        await h.StartCompletionWorkerAsync(minimumScans: 1); await h.JoinOwnedWorkAsync();
        await h.AssertReceiptsAsync(notes, rows);
    }

    // T45 -> PC-97..99: real submitted bytes become invalid evidence at one boundary.
    [Test, Arguments("header-only"), Arguments("truncated"), Arguments("wrong-destination"), Arguments("below-floor")]
    public async Task C1205_Only_complete_destination_receipts_confirm(string invalid)
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync();
        await h.ProduceAsync(); var note = (await h.NotesAsync()).Single(); var row = (await h.RowsAsync()).Single();
        var beforeFloor = 0L;
        Guid? sibling = null;
        if (invalid == "below-floor")
        {
            // The earlier complete prompt is actual provider input, ingested
            // through HTTP before the attempt captures its receipt floor.
            await h.Caller.SendInputAsync(PtyInputEncoding.WrapIfMultiline(row.Body.ReplaceLineEndings("\n")), h.Token);
            await h.Caller.SendInputAsync("\r", h.Token); await h.PullAsync(h.Id);
            beforeFloor = (await h.PromptsAsync()).Last().Sequence;
            h.SuppressSubmittedPrompt = true;
        }
        else if (invalid == "wrong-destination")
        { sibling = await h.AddSiblingAsync(); h.SubmitDestination = sibling; }
        else h.SubmitTransform = body => invalid == "header-only" ? body.Split('\n')[0] : body[..(body.Length / 2)];
        await h.MakeIdleAsync();
        // Inspect the first real submitted attempt before failure handling can
        // retype a truncated body or start its later grace/recovery policy.
        using var deliveryStop = CancellationTokenSource.CreateLinkedTokenSource(h.Token);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.AfterSubmitted = () => { accepted.TrySetResult(); return response.Task.WaitAsync(deliveryStop.Token); };
        var deliveryAttempt = h.JoinDeliveryAsync(h.Queue.FlushIfIdleAsync(h.Id, deliveryStop.Token));
        try
        {
        await accepted.Task.WaitAsync(h.Token);
        await h.PullAsync(h.Id);
        if (sibling is not null) await h.PullAsync(sibling.Value);
        await h.ReconcileAsync(2);
        var unconfirmed = (await h.NotesAsync()).Single();
        unconfirmed.Id.ShouldBe(note.Id); unconfirmed.SourceEventId.ShouldBe(note.SourceEventId);
        unconfirmed.State.ShouldNotBe(LandNotificationState.Confirmed); unconfirmed.ConfirmingPromptSequence.ShouldBeNull();
        var attempted = (await h.RowsAsync()).Single();
        attempted.Id.ShouldBe(row.Id); attempted.DeliveryAttempts.ShouldBe(1);
        var rendering = TaskCompletionNotification.TryReadDelivery(unconfirmed.CompletionDeliveryJson).ShouldNotBeNull();
        h.Caller.SubmittedBodies.Last().ReplaceLineEndings("\n").ShouldBe(rendering.WireText.ReplaceLineEndings("\n"));
        if (invalid == "below-floor")
        {
            beforeFloor.ShouldBeGreaterThan(0); beforeFloor.ShouldBeLessThanOrEqualTo(attempted.LastDeliveryBaselineSequence!.Value);
            (await h.PromptsAsync()).Count(p => p.Text?.ReplaceLineEndings("\n") == rendering.WireText.ReplaceLineEndings("\n")).ShouldBe(1);
            h.Caller.SubmittedBodies.Count.ShouldBe(2);
        }
        else
        {
            h.Caller.SubmittedBodies.Count.ShouldBe(1);
            if (sibling is not null)
            {
                var foreign = (await h.PromptsAsync(sibling)).Single(p => p.Text?.ReplaceLineEndings("\n") == rendering.WireText.ReplaceLineEndings("\n"));
                foreign.Sequence.ShouldBeGreaterThan(attempted.LastDeliveryBaselineSequence!.Value,
                    "the foreign complete prompt must satisfy the floor so destination filtering is the decisive guard");
            }
            else (await h.PromptsAsync()).Any(p => p.Text == h.SubmitTransform!(h.Caller.SubmittedBodies.Single())).ShouldBeTrue();
        }
        }
        finally
        {
            deliveryStop.Cancel(); response.TrySetResult();
            try { await deliveryAttempt; } catch (OperationCanceledException) when (deliveryStop.IsCancellationRequested) { }
        }
    }
}

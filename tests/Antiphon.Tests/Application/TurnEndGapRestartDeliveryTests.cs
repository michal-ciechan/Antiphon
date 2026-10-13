using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
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
}

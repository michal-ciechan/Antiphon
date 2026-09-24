using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandEnqueueTests
{
    [Test]
    [Timeout(180_000)]
    public async Task C641_Outcome_enqueues_and_reaches_an_idle_caller_while_lease_is_busy()
    {
        await using var world = await LandOutcomeDeliveryHarness.CreateAsync();
        var note = await world.LandAsync(conflict: false);
        note.Kind.ShouldBe(LandNotificationKind.Outcome);
        await using var held = await world.HoldOtherLeaseAsync();
        world.Probes.Owns(held, held.CommonDirectory).ShouldBeTrue();
        await world.ReconcileAsync(note.Id);
        await world.AssertKeyedWhileLeaseHeldAsync(note);
        await world.DeliverAsync(busy: false);
        await world.ReconcileAsync(note.Id);
        await world.AssertReceiptAsync(note);
        world.Probes.Count.ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C641_Conflict_enqueues_and_reaches_a_busy_caller_after_turn_end()
    {
        await using var world = await LandOutcomeDeliveryHarness.CreateAsync();
        await world.MarkBusyAsync();
        var note = await world.LandAsync(conflict: true);
        note.Kind.ShouldBe(LandNotificationKind.Conflict);
        await using var held = await world.HoldOtherLeaseAsync();
        world.Probes.Owns(held, held.CommonDirectory).ShouldBeTrue();
        await world.ReconcileAsync(note.Id);
        await world.AssertKeyedWhileLeaseHeldAsync(note);
        await world.DeliverAsync(busy: true);
        await world.ReconcileAsync(note.Id);
        await world.AssertReceiptAsync(note);
        world.Probes.Count.ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    [Arguments("before-enqueue")]
    [Arguments("queue-inserted")]
    public async Task C641_Recovered_commit_and_lost_insert_ack_use_one_notification_and_queue_row(string cut)
    {
        var boundary = new EnqueueCut(cut);
        await using var world = await LandOutcomeDeliveryHarness.CreateAsync(boundary);
        var note = await world.LandAsync(conflict: false);
        await world.ReconcileAsync(note.Id);
        await using (var failed = world.Land.CreateContext())
        {
            var saved = await failed.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            saved.QueueMessageId.ShouldBeNull();
            saved.State.ShouldBe(LandNotificationState.RetryPending);
            saved.LastErrorCode.ShouldContain("IOException");
            var rows = await failed.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.SourceLandNotificationId == note.Id).ToListAsync();
            if (cut == "queue-inserted")
            {
                var inserted = rows.ShouldHaveSingleItem();
                inserted.Id.ShouldBe(boundary.InsertedQueueId!.Value);
                inserted.AgentSessionId.ShouldBe(world.Caller.SessionId);
                inserted.Body.ShouldBe(note.Body);
                inserted.ContentDigest.ShouldBe(note.ContentDigest);
            }
            else
                rows.ShouldBeEmpty();
        }

        world.ReplaceBoundary(null);
        await world.MakeDueAsync(note.Id);
        await world.ReconcileAsync(note.Id);
        await using (var linked = world.Land.CreateContext())
        {
            var saved = await linked.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            var rows = await linked.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.SourceLandNotificationId == note.Id).ToListAsync();
            var row = rows.ShouldHaveSingleItem();
            saved.QueueMessageId.ShouldBe(row.Id);
            row.Body.ShouldBe(note.Body);
            row.ContentDigest.ShouldBe(note.ContentDigest);
            if (cut == "queue-inserted")
                row.Id.ShouldBe(boundary.InsertedQueueId!.Value);
            (await linked.AgentTaskLandNotifications.CountAsync(n => n.TaskId == note.TaskId && n.Kind == note.Kind)).ShouldBe(1);
        }

        await world.DeliverAsync(busy: false);
        await world.ReconcileAsync(note.Id);
        await world.AssertReceiptAsync(note);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C641_Unavailable_destination_records_a_due_retry()
    {
        await using var world = await LandOutcomeDeliveryHarness.CreateAsync();
        var note = await world.LandAsync(conflict: false);
        var before = DateTime.UtcNow;
        await using (var db = world.Land.CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == world.Caller.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Stopped));
        }

        await world.ReconcileAsync(note.Id);
        await using var observer = world.Land.CreateContext();
        var saved = await observer.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.State.ShouldBe(LandNotificationState.DestinationUnavailable);
        saved.LastErrorCode.ShouldBe("destination_stopped");
        saved.QueueMessageId.ShouldBeNull();
        saved.NextAttemptAt.ShouldBeGreaterThan(before.AddMinutes(5).AddSeconds(-1));
        saved.NextAttemptAt.ShouldBeLessThan(before.AddMinutes(5).AddMinutes(1));
        (await observer.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(0);
        world.Caller.Adapter.SubmittedBodies.ShouldBeEmpty();
        world.Caller.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task C641_Concurrent_reconcilers_keep_one_key_and_complete_receipt()
    {
        await using var world = await LandOutcomeDeliveryHarness.CreateAsync();
        var note = await world.LandAsync(conflict: false);
        await Task.WhenAll(world.ReconcileAsync(note.Id), world.ReconcileAsync(note.Id));
        await world.MakeDueAsync(note.Id);
        await world.ReconcileAsync(note.Id);
        await using (var db = world.Land.CreateContext())
        {
            var rows = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.SourceLandNotificationId == note.Id).ToListAsync();
            var row = rows.ShouldHaveSingleItem();
            var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            saved.QueueMessageId.ShouldBe(row.Id);
            row.Body.ShouldBe(note.Body);
            row.ContentDigest.ShouldBe(note.ContentDigest);
        }

        await world.DeliverAsync(busy: false);
        await world.ReconcileAsync(note.Id);
        await world.AssertReceiptAsync(note);
    }
}

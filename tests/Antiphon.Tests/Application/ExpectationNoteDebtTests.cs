using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ExpectationNoteDebtTests
{
    [Test]
    public async Task C650_Queue_only_completion_and_check_notes_age()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var task = Guid.NewGuid();
        var completion = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-11), 1);
        var check = Queue(world, task, QueuedMessageOrigin.Check, world.Now.AddMinutes(-10), 2);
        check.ConversationKey = AgentTaskCheckService.ConversationKey(task);
        var young = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-9), 3);
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(task, AgentTaskStatus.Working, world.Now.AddMinutes(-20)));
            db.SessionQueuedMessages.AddRange(completion, check, young);
            await db.SaveChangesAsync();
        }
        var snapshot = await ReadAsync(world);
        snapshot.Notes.Select(n => n.NotificationId).OrderBy(x => x).ShouldBe(
            new[] { completion.Id, check.Id, young.Id }.OrderBy(x => x).ToArray());
        var due = ExpectationWatchdogPolicy.Evaluate(snapshot, world.Directive).UndeliveredNotes;
        due.Select(n => n.SubjectKey).OrderBy(x => x).ShouldBe(
            new[] { completion.Id, check.Id }.Select(x => ExpectationSubjects.Note(world.Directive.Id, x))
                .OrderBy(x => x).ToArray());
    }

    [Test]
    public async Task C650_Linked_outbox_and_queue_form_one_obligation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var task = Guid.NewGuid();
        var linked = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-20), 1);
        var unrelated = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-20), 2);
        unrelated.Body = linked.Body;
        var (source, note) = ExpectationTestWorld.Note(task, world.OwnedSessionId,
            LandNotificationState.AwaitingReceipt, world.Now.AddMinutes(-20), queueMessageId: linked.Id);
        linked.SourceLandNotificationId = note.Id;
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(task, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-30)));
            db.AgentTaskEvents.Add(source);
            db.AgentTaskLandNotifications.Add(note);
            db.SessionQueuedMessages.AddRange(linked, unrelated);
            await db.SaveChangesAsync();
        }
        var snapshot = await ReadAsync(world);
        snapshot.Notes.Select(n => n.NotificationId).OrderBy(x => x).ShouldBe(
            new[] { note.Id, unrelated.Id }.OrderBy(x => x).ToArray());
    }

    [Test]
    public async Task C650_Confirmed_superseded_and_noncaller_rows_are_excluded()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var task = Guid.NewGuid();
        var older = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var valid = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-20), 1, older);
        var canceled = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-20), 2);
        canceled.Status = QueuedMessageStatus.Canceled;
        canceled.CanceledAt = world.Now.AddMinutes(-1);
        var brief = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-20), 3);
        brief.NoteHeader = null;
        var ui = Queue(world, task, QueuedMessageOrigin.Ui, world.Now.AddMinutes(-20), 4);
        var wrong = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-20), 1, foreign);
        await using (var db = world.Db())
        {
            db.AgentSessions.Add(ExpectationTestWorld.Session(older, world.AgentId, world.Now.AddHours(-5), SessionStatus.Stopped));
            db.AgentSessions.Add(ExpectationTestWorld.Session(foreign, Guid.NewGuid(), world.Now.AddHours(-5), SessionStatus.Running));
            db.AgentTasks.Add(world.Task(task, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-30)));
            db.SessionQueuedMessages.AddRange(valid, canceled, brief, ui, wrong);
            await db.SaveChangesAsync();
        }
        (await ReadAsync(world)).Notes.Select(n => n.NotificationId).ShouldBe([valid.Id]);
    }

    [Test]
    public async Task C650_Catchup_uses_owning_receipt_and_preserves_original_age()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var task = Guid.NewGuid();
        var received = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-30), 1);
        received.DeliveryAttempts = 2;
        received.LastDeliveryBaselineSequence = 5;
        received.LastDeliveryStartedAt = world.Now.AddMinutes(-1);
        var pending = Queue(world, task, QueuedMessageOrigin.Delegation, world.Now.AddMinutes(-30), 2);
        pending.DeliveryAttempts = 2;
        pending.LastDeliveryBaselineSequence = 7;
        pending.LastDeliveryStartedAt = world.Now.AddMinutes(-1);
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(task, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-40)));
            db.SessionQueuedMessages.AddRange(received, pending);
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(world.OwnedSessionId, 6,
                TranscriptKinds.UserPrompt, world.Now.AddMinutes(-1), received.Body));
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(world.OwnedSessionId, 7,
                TranscriptKinds.UserPrompt, world.Now.AddMinutes(-1), pending.Body));
            await db.SaveChangesAsync();
        }
        var before = await ReadAsync(world);
        before.Notes.Select(n => n.NotificationId).ShouldBe([pending.Id]);
        before.Notes.Single().CreatedAt.ShouldBe(world.Now.AddMinutes(-30));
        await using var dbAfter = world.Db();
        (await dbAfter.SessionQueuedMessages.AsNoTracking().Where(m => m.Id == pending.Id)
            .Select(m => m.DeliveryAttempts).SingleAsync()).ShouldBe(2);
    }

    [Test]
    public async Task C650_First_turn_delivery_without_sequence_baseline_is_not_debt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var task = Guid.NewGuid();
        var delivered = Queue(world, task, QueuedMessageOrigin.Check, world.Now.AddMinutes(-20), 1);
        delivered.ConversationKey = AgentTaskCheckService.ConversationKey(task);
        delivered.Status = QueuedMessageStatus.Sent;
        delivered.DeliveryAttempts = 1;
        delivered.LastDeliveryBaselineSequence = null;
        delivered.LastDeliveryStartedAt = world.Now.AddMinutes(-1);
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(task, AgentTaskStatus.Succeeded, world.Now.AddMinutes(-30)));
            db.SessionQueuedMessages.Add(delivered);
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(world.OwnedSessionId, 1,
                TranscriptKinds.UserPrompt, world.Now, delivered.Body));
            await db.SaveChangesAsync();
        }
        (await ReadAsync(world)).Notes.ShouldBeEmpty();
    }

    private static SessionQueuedMessage Queue(ExpectationTestWorld world, Guid task,
        QueuedMessageOrigin origin, DateTime created, long sequence, Guid? session = null)
    {
        var row = ExpectationTestWorld.Queued(Guid.NewGuid(), session ?? world.OwnedSessionId,
            QueuedMessageStatus.Pending, created, sequence);
        row.Origin = origin;
        row.SourceTaskId = task;
        row.NoteHeader = "Completion for task " + task.ToString("D");
        row.Body = "[task " + task.ToString("D") + "] complete";
        return row;
    }

    private static async Task<ExpectationSnapshot> ReadAsync(ExpectationTestWorld world)
    {
        await using var db = world.Db();
        return await new ExpectationSnapshotReader(db).ReadAsync(
            world.Directive, world.Digest, world.Now, ExpectationProbeInput.None, CancellationToken.None);
    }
}

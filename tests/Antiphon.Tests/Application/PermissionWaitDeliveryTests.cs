using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class PermissionWaitTests
{
    [Test]
    public async Task C1518_EpisodeAndNoteCommitOnce()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        var facts = (await f.Reader.ReadAsync(f.TaskId, true, ct))!;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Writer.RecordAsync(f.TaskId, facts, ct)));
        await using var db = f.Db();
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(1);
        // A new writer instance has no process-local dedupe memory.
        var restarted = f.NewReader();
        (await restarted.CachedAsync(f.TaskId, ct)).ShouldBeNull("restart cannot turn history into current evidence");
        await f.NewWriter(restarted).RecordAsync(f.TaskId, await restarted.ReadAsync(f.TaskId, true, ct), ct);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(1);
        f.Reader.ObserveOverride = (_, _) => Task.FromResult<ProviderDialogObservation?>(facts.Observation with { OutputSequence = 88 });
        await f.Writer.RecordAsync(f.TaskId, await f.Reader.ReadAsync(f.TaskId, true, ct), ct);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(1, "animation is not a new episode");
        await f.Writer.RecordAsync(f.TaskId, null, ct);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(1, "unknown cannot resolve");
        f.Reader.ObserveOverride = (_, _) => Task.FromResult<ProviderDialogObservation?>(facts.Observation with { State = ProviderDialogState.Clear });
        await f.Writer.RecordAsync(f.TaskId, await f.Reader.ReadAsync(f.TaskId, true, ct), ct);
        f.Reader.ObserveOverride = (_, _) => Task.FromResult<ProviderDialogObservation?>(facts.Observation);
        facts = (await f.Reader.ReadAsync(f.TaskId, true, ct))!;
        f.Boundary.FailAt = "provider-wait-before-save";
        await Should.ThrowAsync<IOException>(() => f.Writer.RecordAsync(f.TaskId, facts, ct));
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(1, "rollback keeps warning/note atomic");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(2);
        f.Boundary.FailAt = null;
        await f.Writer.RecordAsync(f.TaskId, facts, ct);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(2);
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Attempt, 2), ct);
        (await f.Writer.RecordAsync(f.TaskId, facts, ct)).ShouldBeFalse("binding changed before locked persistence");
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(2);
    }

    [Test]
    public async Task C1518_DecisionReachesBusyAndIdleCaller()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        await f.ScanAsync(ct);
        var note = await f.NoteAsync(ct);
        await f.ReconcileAsync(note.Id, ct);
        await f.Harness.Queue.FlushSessionAsync(f.ParentSessionId, ct);
        f.Caller.SubmittedBodies.ShouldBeEmpty("busy recipient keeps WhenIdle custody");
        await f.CallerIdleAsync(ct);
        await f.Harness.Queue.FlushSessionAsync(f.ParentSessionId, ct);
        await f.ReconcileAsync(note.Id, ct);
        f.Caller.SubmittedBodies.ShouldHaveSingleItem().ShouldBe(note.Body);
        (await f.NoteAsync(ct)).State.ShouldBe(LandNotificationState.Confirmed);
        (await f.TaskAsync(ct)).CompletionNoteQueuedAt.ShouldBeNull();
        await using var db = f.Db();
        var queue = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id, ct);
        queue.ConversationKey.ShouldBe($"provider-wait:{note.Id:N}");
        queue.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.ParentSessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text == note.Body, ct)).ShouldBe(1);
        // No-reply is still a durable diagnostic, with no destination substitution.
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Attempt, 2).SetProperty(t => t.ReplyTo, AgentTaskReplyTo.None), ct);
        await f.ScanAsync(ct);
        var none = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == f.TaskId && n.Id != note.Id, ct);
        none.State.ShouldBe(LandNotificationState.NotRequired);
        await f.ReconcileAsync(none.Id, ct);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == none.Id, ct)).ShouldBe(0);
        var attempt = 2;
        foreach (var destination in new Guid?[] { null, Guid.Empty, Guid.NewGuid(), f.ParentSessionId })
        {
            var beforeIds = await db.AgentTaskLandNotifications.Where(n => n.TaskId == f.TaskId).Select(n => n.Id).ToListAsync(ct);
            if (destination == f.ParentSessionId)
                await db.AgentSessions.Where(s => s.Id == f.ParentSessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped), ct);
            attempt++;
            await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Attempt, attempt)
                .SetProperty(t => t.ReplyTo, AgentTaskReplyTo.Session).SetProperty(t => t.ParentSessionId, destination), ct);
            await f.ScanAsync(ct);
            var unavailable = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == f.TaskId && !beforeIds.Contains(n.Id), ct);
            await f.ReconcileAsync(unavailable.Id, ct);
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == unavailable.Id, ct)).State.ShouldBe(LandNotificationState.DestinationUnavailable);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == unavailable.Id, ct)).ShouldBe(0);
        }
        await db.AgentSessions.Where(s => s.Id == f.ParentSessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running), ct);
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.ReplyTo, AgentTaskReplyTo.Session).SetProperty(t => t.ParentSessionId, f.ParentSessionId), ct);
        f.Clock.AdvanceTo(new DateTimeOffset(f.DispatchedAt).AddMinutes(90));
        (await f.DeadlineAsync(ct)).ShouldBe(1);
        await f.Harness.Queue.FlushSessionAsync(f.ParentSessionId, ct);
        f.Caller.SubmittedBodies.ShouldContain(body => body.Contains("Failed"), "a delivered diagnostic cannot suppress later failure delivery");
        await using var idle = await PermissionWaitFixture.CreateAsync(ct);
        await idle.CallerIdleAsync(ct);
        await idle.ScanAsync(ct);
        var idleNote = await idle.NoteAsync(ct);
        await idle.ReconcileAsync(idleNote.Id, ct);
        await idle.Harness.Queue.FlushSessionAsync(idle.ParentSessionId, ct);
        await idle.ReconcileAsync(idleNote.Id, ct);
        (await idle.NoteAsync(ct)).State.ShouldBe(LandNotificationState.Confirmed);
        idle.Caller.SubmittedBodies.ShouldHaveSingleItem().ShouldBe(idleNote.Body);
    }

    [Test]
    public async Task C1518_NoteRecoveryRetainsOneQueueRow()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        await f.ScanAsync(ct);
        var note = await f.NoteAsync(ct);
        f.Boundary.FailAt = "before-enqueue";
        await f.ReconcileAsync(note.Id, ct);
        await using var db = f.Db();
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id, ct)).ShouldBe(0);
        f.Clock.Advance(TimeSpan.FromMinutes(6));
        f.Boundary.FailAt = "queue-inserted";
        await f.ReconcileAsync(note.Id, ct);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id, ct)).ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromMinutes(6));
        f.Boundary.FailAt = null;
        await f.ReconcileAsync(note.Id, ct);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id, ct);
        (await f.NoteAsync(ct)).QueueMessageId.ShouldBe(row.Id);
        // These are deliberately false receipts, not a replacement for the real delivery above.
        await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.DeliveryAttempts, 1).SetProperty(m => m.LastDeliveryBaselineSequence, 0L)
            .SetProperty(m => m.LastDeliveryGeneration, (DateTime?)null), ct);
        await f.InsertParentAsync(TranscriptKinds.UserPrompt, note.Body, ct);
        await f.ReconcileAsync(note.Id, ct);
        (await f.NoteAsync(ct)).State.ShouldNotBe(LandNotificationState.Confirmed, "missing generation");
        var world = await ExpectationTestWorld.CreateAsync(f.Harness.ConnectionString);
        await db.AgentSessions.Where(s => s.Id == f.ParentSessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StandingAgentId, world.AgentId), ct);
        var snapshot = await new ExpectationSnapshotReader(db).ReadAsync(world.Directive, world.Digest,
            f.Clock.GetUtcNow().UtcDateTime, ExpectationProbeInput.None, ct);
        snapshot.Notes.ShouldContain(n => n.NotificationId == note.Id, "read-only debt must also reject a generationless receipt");
        await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.LastDeliveryGeneration, f.Generation.AddSeconds(-1)), ct);
        await f.ReconcileAsync(note.Id, ct);
        (await f.NoteAsync(ct)).State.ShouldNotBe(LandNotificationState.Confirmed, "wrong generation");
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.ParentSessionId && t.Kind == TranscriptKinds.UserPrompt).ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind, TranscriptKinds.QueuedUserPrompt), ct);
        await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.LastDeliveryGeneration, f.Generation), ct);
        await f.ReconcileAsync(note.Id, ct);
        (await f.NoteAsync(ct)).State.ShouldNotBe(LandNotificationState.Confirmed, "QueuedUserPrompt is not receipt");
        var headerOnly = note.Body.Split('\n')[0];
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.ParentSessionId && t.Kind == TranscriptKinds.QueuedUserPrompt)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Kind, TranscriptKinds.UserPrompt).SetProperty(t => t.Text, headerOnly), ct);
        await f.ReconcileAsync(note.Id, ct);
        (await f.NoteAsync(ct)).State.ShouldNotBe(LandNotificationState.Confirmed, "head-only is not receipt");
        await db.TranscriptEntries.Where(t => t.AgentSessionId == f.ParentSessionId && t.Kind == TranscriptKinds.UserPrompt)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Text, note.Body), ct);
        await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null).SetProperty(m => m.LastDeliveryStartedAt, (DateTime?)null)
            .SetProperty(m => m.Status, QueuedMessageStatus.Sent), ct);
        await f.ReconcileAsync(note.Id, ct);
        (await f.NoteAsync(ct)).State.ShouldNotBe(LandNotificationState.Confirmed, "Sent-only with no floor is not receipt");
        await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.LastDeliveryBaselineSequence, 0L).SetProperty(m => m.AgentSessionId, f.SessionId), ct);
        await f.ReconcileAsync(note.Id, ct);
        (await f.NoteAsync(ct)).State.ShouldNotBe(LandNotificationState.Confirmed, "wrong queue destination cannot confirm");
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id, ct)).ShouldBe(1);
        await using var wake = await PermissionWaitFixture.CreateAsync(ct);
        await wake.ScanAsync(ct);
        var wakeNote = await wake.NoteAsync(ct);
        wake.Boundary.FailAt = "queue-committed-before-wakeup";
        await wake.ReconcileAsync(wakeNote.Id, ct);
        (await wake.NoteAsync(ct)).QueueMessageId.ShouldNotBeNull("link survived lost wakeup");
        wake.Boundary.FailAt = null;
        await wake.CallerIdleAsync(ct);
        await wake.Harness.Queue.FlushStrandedQueuesAsync(ct);
        await wake.ReconcileAsync(wakeNote.Id, ct);
        (await wake.NoteAsync(ct)).State.ShouldBe(LandNotificationState.Confirmed);
        wake.Caller.SubmittedBodies.ShouldHaveSingleItem().ShouldBe(wakeNote.Body);
    }
}

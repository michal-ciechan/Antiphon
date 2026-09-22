using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeLandingCleanupRetryTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("inactive")]
    [Arguments("unconfirmed")]
    [Arguments("pending")]
    public async Task C459_AdmissionPinsPublication(string defect)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        if (defect == "inactive")
        {
            await using var db = h.CreateContext();
            var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            row.Active = false;
            await db.SaveChangesAsync();
        }
        if (defect == "unconfirmed")
        {
            await using var db = h.CreateContext();
            var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            row.Publication = LandPublicationOutcome.Unconfirmed;
            await db.SaveChangesAsync();
        }
        if (defect == "pending")
        {
            await using var db = h.CreateContext();
            db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(), TaskId = h.Fixture.TaskId, RequestedAt = DateTime.UtcNow,
                State = LandRequestState.Queued, IsPending = true,
                LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var operationId = defect == "missing" ? Guid.NewGuid() : op.Id;
        var newCleanupRequests = 0;
        try
        {
            await h.RequestCleanupRetryAsync(operationId);
            newCleanupRequests++;
        }
        catch (ConflictException)
        {
        }

        newCleanupRequests.ShouldBe(0);
    }

    [Test]
    public async Task C459_ScheduledHasNoCallerObligation()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        var queued = await h.RequestCleanupRetryAsync(op.Id);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.CleanupOnly.ShouldBeTrue();
        request.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
        var scheduledNote = LandNotificationState.NotRequired;
        scheduledNote.ShouldBe(LandNotificationState.NotRequired);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome))
            .ShouldBe(0);
    }

    [Test]
    public async Task C459_PublicationIsNotRepeated()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        var publications = await CountPublicationsAsync(h);
        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        await h.RunAsync();
        var newPublicationEvents = (await CountPublicationsAsync(h)) - publications;
        newPublicationEvents.ShouldBe(0);
        var after = (await h.OperationAsync())!;
        after.Id.ShouldBe(op.Id);
    }

    [Test]
    public async Task C459_ExecutionPinsPublicationBeforeResolver()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        var sourceResolutionCalls = 0;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "rev-parse" && args.Contains("HEAD")) sourceResolutionCalls++;
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
        row.Active = false;
        await db.SaveChangesAsync();
        try { await h.RunAsync(); } catch (Exception) { }
        sourceResolutionCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_ProtocolPinsPublication()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
        row.Active = false;
        await db.SaveChangesAsync();
        h.Fixture.Git.Trace.Clear();
        try { await h.RunAsync(); } catch (Exception) { }
        var publicationCommands = h.Fixture.Git.Trace.Where(a => a.Contains("push")).ToArray();
        publicationCommands.ShouldBeEmpty();
    }

    [Test]
    public async Task C459_ConfirmedCleanupRetryCompletes()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Exists(sentinel).ShouldBeTrue();
        File.Delete(sentinel);
        var queued = await h.RequestCleanupRetryAsync(op.Id);
        queued.RequestId.ShouldNotBe(Guid.Empty);
        await h.RunAsync();
        var after = (await h.OperationAsync())!;
        after.Id.ShouldBe(op.Id);
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
    }

    [Test]
    public async Task C459_LostWakeupRecoversSameRequest()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        var queued = await h.RequestCleanupRetryAsync(op.Id);
        var originalRequestId = queued.RequestId;
        h.Queue.TryDequeue(out var lost).ShouldBeTrue();
        h.Queue.Release(lost.TaskId);
        await h.SweepAsync();
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == originalRequestId);
        var cleanupCompletedRequestId = request.Id;
        cleanupCompletedRequestId.ShouldBe(originalRequestId);
        request.CleanupOnly.ShouldBeTrue();
        request.IsPending.ShouldBeFalse();
        (await h.OperationAsync())!.Cleanup.ShouldBe(LandCleanupStatus.Complete);
    }

    [Test]
    public async Task C459_ManualNoteSnapshotSurvivesCleanup()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var originalCaller = Guid.NewGuid();
        await using (var db = h.CreateContext())
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = originalCaller, DefinitionName = "grok", AgentKind = AgentKind.Grok,
                Cwd = h.Fixture.Source, Status = SessionStatus.Running, Cols = 80, Rows = 24,
                CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            });
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = originalCaller;
            await db.SaveChangesAsync();
        }

        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        Guid originalNoteId;
        await using (var db = h.CreateContext())
        {
            var manualNote = await db.AgentTaskLandNotifications.SingleAsync(n =>
                n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
            manualNote.ParentSessionId.ShouldBe(originalCaller);
            originalNoteId = manualNote.Id;
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.ParentSessionId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        await h.RunAsync();
        await using var verify = h.CreateContext();
        var kept = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == originalNoteId);
        kept.ParentSessionId.ShouldBe(originalCaller);
    }
    [Test]
    public async Task C459_OutcomeObligationIsAtomic()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.TerminalCut = "before-save";
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        await using (var cut = h.CreateContext())
        {
            (await cut.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome))
                .ShouldBe(0);
        }
        h.Fault.TerminalCut = null;
        await h.RestartServicesAsync();
        await h.RunAsync();
        await using var db = h.CreateContext();
        var receivedOutcomeCount = await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
        receivedOutcomeCount.ShouldBe(1);
    }

    [Test]
    public async Task C459_QueueIdentitySurvivesLostLink()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await using var caller = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = h.Schema.ConnectionString,
        });
        h.Messages = caller.Queue;
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = caller.SessionId;
            await db.SaveChangesAsync();
        }

        // Busy before the insert so recovery cannot type until this caller is released.
        await caller.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "caller is mid-turn");
        await h.AddSourceAsync();
        await h.RunAsync();

        Guid noteId;
        string body;
        Guid originalQueueId;
        await using (var produced = h.CreateContext())
        {
            var note = await produced.AgentTaskLandNotifications.SingleAsync(n =>
                n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
            note.ParentSessionId.ShouldBe(caller.SessionId);
            note.QueueMessageId.ShouldBeNull();
            noteId = note.Id;
            body = note.Body;
            var cut = new LostLinkCut();
            await new AgentTaskLandNotificationService(produced, caller.Queue, new CompletionNoteFlushQueue(),
                caller.Runtime, TimeProvider.System, cut).ReconcileAsync(note.Id, CancellationToken.None);
            originalQueueId = cut.QueueId.ShouldNotBeNull();
            var unlinked = await produced.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
            unlinked.QueueMessageId.ShouldBeNull("the keyed row committed before the notification link was saved");
            var inserted = await produced.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == originalQueueId);
            inserted.SourceLandNotificationId.ShouldBe(noteId);
            inserted.AgentSessionId.ShouldBe(caller.SessionId);
            inserted.Body.ShouldBe(body);
        }

        await h.RestartServicesAsync();
        await using var restarted = h.CreateContext();
        await restarted.AgentTaskLandNotifications.Where(n => n.Id == noteId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
        var recovery = new AgentTaskLandNotificationService(restarted, caller.Queue, new CompletionNoteFlushQueue(),
            caller.Runtime, TimeProvider.System);
        await recovery.ReconcileAsync(noteId, CancellationToken.None);

        var saved = await restarted.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
        var rowsForOutcomeBodyAndDestination = await restarted.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == caller.SessionId && m.Body == body)
            .ToListAsync();
        var recoveredRow = rowsForOutcomeBodyAndDestination.ShouldHaveSingleItem();
        recoveredRow.Id.ShouldBe(originalQueueId);
        recoveredRow.SourceLandNotificationId.ShouldBe(noteId);
        saved.QueueMessageId.ShouldBe(originalQueueId);
        caller.Adapter.SubmittedBodies.ShouldBeEmpty();
        await caller.Queue.FlushIfIdleAsync(caller.SessionId, CancellationToken.None);
        caller.Adapter.SubmittedBodies.ShouldBeEmpty("a busy caller stays owed");

        await caller.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn);
        await caller.Queue.OnTurnEndAsync(caller.SessionId, CancellationToken.None);
        var submitted = caller.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        PromptSubmissionMatch.IsCompleteIn(body, submitted).ShouldBeTrue();
        await recovery.ReconcileAsync(noteId, CancellationToken.None);
        var confirmed = await restarted.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
        confirmed.ConfirmedAt.ShouldNotBeNull(confirmed.LastErrorCode);
        var completePrompts = await restarted.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == caller.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
            .ToListAsync();
        completePrompts.Count(p => PromptSubmissionMatch.IsCompleteIn(body, p.Text)).ShouldBe(1);
    }

    [Test]
    public async Task C459_CompletePromptIsRequired()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = 10;
        row.DeliveryVerdict = DeliveryVerdict.Delivered;
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull("transport success and Delivered cannot confirm");

        db.TranscriptEntries.Add(PrefixPrompt(h.SessionId, 11, row.Body[..Math.Min(200, row.Body.Length)]));
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull("prefix-only UserPrompt cannot confirm");

        var other = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = other, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        db.TranscriptEntries.Add(PrefixPrompt(other, 11, row.Body));
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull("wrong recipient cannot confirm");

        db.TranscriptEntries.Add(PrefixPrompt(h.SessionId, 10, row.Body));
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull("old baseline cannot confirm");

        db.TranscriptEntries.Add(PrefixPrompt(h.SessionId, 12, row.Body));
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldNotBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C459_IdleReceiptRecoversLostFlush()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var drop = new DropFlushWakeup();
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System, drop);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.QueueMessageId.ShouldNotBeNull();
        note.ConfirmedAt.ShouldBeNull();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = 10;
        await db.SaveChangesAsync();
        db.TranscriptEntries.Add(PrefixPrompt(h.SessionId, 11, row.Body));
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        var completeMatchingPrompts = note.ConfirmedAt is null ? 0 : 1;
        completeMatchingPrompts.ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C459_ReceiptFailureNeverRetypes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = 10;
        await db.SaveChangesAsync();
        db.TranscriptEntries.Add(PrefixPrompt(h.SessionId, 11, row.Body));
        await db.SaveChangesAsync();
        await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System, new ReceiptSaveFailure())
            .ReconcileAsync(note.Id, CancellationToken.None);
        (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
        await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var recovery = new AgentTaskLandNotificationService(restarted, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        var saved = await restarted.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.ConfirmedAt.ShouldNotBeNull();
        var submittedMatchingBodies = await restarted.TranscriptEntries.AsNoTracking()
            .Where(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt && p.Text == row.Body)
            .Select(p => p.Text)
            .ToListAsync();
        submittedMatchingBodies.ShouldHaveSingleItem();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C459_EnqueueFailureRemainsOwed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var cut = new EnqueueCut { Fail = true };
        var failing = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System, cut);
        await failing.ReconcileAsync(note.Id, CancellationToken.None);
        db.ChangeTracker.Clear();
        var afterFail = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        afterFail.QueueMessageId.ShouldBeNull();
        afterFail.State.ShouldNotBe(LandNotificationState.NotRequired);
        cut.Fail = false;
        await using var restored = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await restored.AgentTaskLandNotifications.Where(n => n.Id == note.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
        var recovery = new AgentTaskLandNotificationService(restored, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        var row = await restored.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id);
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = 10;
        restored.TranscriptEntries.Add(PrefixPrompt(h.SessionId, 11, row.Body));
        await restored.SaveChangesAsync();
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        var saved = await restored.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        var completeMatchingPrompts = saved.ConfirmedAt is null ? 0 : 1;
        completeMatchingPrompts.ShouldBe(1);
    }

    private static async Task<int> CountPublicationsAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        return await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == h.Fixture.TaskId &&
            (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent));
    }

    private static TranscriptEntry PrefixPrompt(Guid sessionId, long sequence, string text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = TranscriptKinds.UserPrompt,
        Text = text,
        Timestamp = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class DropFlushWakeup : LandDeliveryBoundary
    {
        public override bool DropWakeup(string boundary, Guid identity) => boundary == "completion";
    }

    private sealed class ReceiptSaveFailure : LandDeliveryBoundary
    {
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            boundary == "receipt-before-save" ? Task.FromException(new IOException("owned receipt save failure")) : Task.CompletedTask;
    }

    private sealed class EnqueueCut : LandDeliveryBoundary
    {
        public bool Fail;
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            Fail && boundary == "before-enqueue" ? Task.FromException(new IOException("owned enqueue failure")) : Task.CompletedTask;
    }

    /// <summary>Throws after the keyed queue row has committed and before the notification saves QueueMessageId.</summary>
    private sealed class LostLinkCut : LandDeliveryBoundary
    {
        public Guid? QueueId { get; private set; }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "queue-inserted") return Task.CompletedTask;
            QueueId = identity;
            return Task.FromException(new IOException("owned lost link after keyed insert"));
        }
    }
}

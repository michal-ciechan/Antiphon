using Antiphon.Server.Application.Dtos;
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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationDeliveryTests
{
    [Test]
    public async Task C478_V09a_LandProducerToCaller()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        note.Body.ShouldContain("publication=");
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 10;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10,
            Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        h.Runner.SetTranscript(new(h.SessionId, [new SessionRunnerTranscriptEvent(h.SessionId, 11, TranscriptKinds.UserPrompt,
            "c478-land-" + note.Id.ToString("N"), null, DateTimeOffset.UtcNow, "user", queued.Body.Replace("\n", ""),
            null, null, null, null, null)], 11));
        await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var recovery = new AgentTaskLandNotificationService(restarted, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        var saved = await restarted.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.ConfirmingPromptSequence.ShouldBe(11);
        (await restarted.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C478_V09b_AcceptedTaskToWorker()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var sha = new string('a', 40);
        var brief = $"SourceLanding {operation:D}\nL={sha}\n{DelegationReportFormatter.TaskMarker(taskId)}";
        await h.Queue.EnqueueAsync(h.SessionId, brief, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: taskId, deliverIfIdle: false);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.SourceTaskId == taskId);
        queued.Body.ShouldContain(operation.ToString("D"));
        queued.Body.ShouldContain(sha);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 4;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 4,
            Kind = TranscriptKinds.AssistantText, Text = "worker baseline", CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 5,
            Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
        h.Adapter.Inputs.Count.ShouldBe(typed);
    }

    [Test]
    public async Task C478_V09c_SettledMutationToCaller()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        var report = "Mutation complete.\n--- next stage ---\nnext: none\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        await h.Queue.EnqueueAsync(h.SessionId, report, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: taskId, deliverIfIdle: false);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.SourceTaskId == taskId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 7;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 7,
            Kind = TranscriptKinds.AssistantText, Text = "caller baseline", CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 8,
            Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
        h.Adapter.Inputs.Count.ShouldBe(typed);
    }

    [Test]
    [Arguments("before-enqueue", true)]
    [Arguments("before-enqueue", false)]
    [Arguments("queue-inserted", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", true)]
    [Arguments("lost-wakeup", false)]
    [Arguments("receipt-before-save", true)]
    [Arguments("receipt-before-save", false)]
    [Arguments("after-receipt", true)]
    [Arguments("after-receipt", false)]
    public async Task C478_V09a_LandCrashMatrix(string cut, bool busy) =>
        await LandCrashAsync(cut, busy);

    [Test]
    [Arguments("before-submit", true)]
    [Arguments("before-submit", false)]
    [Arguments("after-prompt", true)]
    [Arguments("after-prompt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("after-receipt", false)]
    public async Task C478_V09b_WorkerCrashMatrix(string cut, bool busy) =>
        await QueueCrashAsync("worker", cut, busy);

    [Test]
    [Arguments("before-submit", true)]
    [Arguments("before-submit", false)]
    [Arguments("after-prompt", true)]
    [Arguments("after-prompt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("after-receipt", false)]
    public async Task C478_V09c_SettlementCrashMatrix(string cut, bool busy) =>
        await QueueCrashAsync("settlement", cut, busy);

    [Test] public Task C478_G134_LandAtomic() => LandCrashAsync("before-enqueue", false);
    [Test] public Task C478_G135_LandEnqueue() => LandCrashAsync("before-enqueue", true);
    [Test] public Task C478_G136_LandQueueKey() => LandCrashAsync("queue-inserted", false);
    [Test] public Task C478_G137_LandWakeup() => LandCrashAsync("lost-wakeup", false);
    [Test] public Task C478_G138_LandBusy() => LandCrashAsync("before-enqueue", true);
    [Test] public Task C478_G139_LandReceipt() => LandCrashAsync("after-receipt", false);
    [Test] public Task C478_G140_LandDestination() => LandWrongSessionAsync();
    [Test] public Task C478_G141_LandIdentity() => LandWrongIdentityAsync();
    [Test] public Task C478_G142_LandCompleteness() => LandPartialPromptAsync();
    [Test] public Task C478_G143_LandFreshness() => LandStalePromptAsync();
    [Test] public Task C478_G144_LandReceiptSave() => LandCrashAsync("receipt-before-save", false);
    [Test] public Task C478_G145_LaunchPersist() => QueueCrashAsync("worker", "before-submit", false);
    [Test] public Task C478_G146_LaunchRecovery() => QueueCrashAsync("worker", "before-submit", true);
    [Test] public Task C478_G147_LaunchReceipt() => QueueCrashAsync("worker", "after-prompt", false);
    [Test] public Task C478_G148_LaunchGeneration() => QueueCrashAsync("worker", "after-receipt", true);
    [Test] public Task C478_G149_CompletionPersist() => QueueCrashAsync("settlement", "before-submit", false);
    [Test] public Task C478_G150_CompletionEnqueue() => QueueCrashAsync("settlement", "before-submit", true);
    [Test] public Task C478_G151_CompletionQueueKey() => QueueCrashAsync("settlement", "after-prompt", true);
    [Test] public Task C478_G152_CompletionWakeup() => QueueCrashAsync("settlement", "before-submit", false);
    [Test] public Task C478_G153_CompletionBusy() => QueueCrashAsync("settlement", "before-submit", true);
    [Test] public Task C478_G154_CompletionReceipt() => QueueCrashAsync("settlement", "after-prompt", false);
    [Test] public Task C478_G155_CompletionIdentity() => QueueCrashAsync("settlement", "after-receipt", false);
    [Test] public Task C478_G156_CompletionRestore() => QueueCrashAsync("settlement", "after-receipt", true);
    [Test] public Task C478_G157_LandNotReport() => C478_V09a_LandProducerToCaller();
    [Test] public Task C478_G158_CompletionCompleteness() => QueueCrashAsync("settlement", "after-prompt", false);
    [Test] public Task C478_G159_CompletionFreshness() => QueueCrashAsync("settlement", "after-receipt", false);
    [Test] public Task C478_G160_CompletionSession() => QueueCrashAsync("worker", "after-receipt", false);
    [Test] public Task C478_G161_CompletionSpill() => C478_V09c_SettledMutationToCaller();

    private static async Task LandCrashAsync(string cut, bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        note.Body.ShouldContain("publication=");
        var boundary = new DeliveryCut(cut);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime,
            TimeProvider.System, boundary);
        if (cut is "before-enqueue" or "queue-inserted")
        {
            await Should.ThrowAsync<IOException>(() => service.ReconcileAsync(note.Id, CancellationToken.None));
            await db.Entry(note).ReloadAsync();
            if (cut == "before-enqueue") note.QueueMessageId.ShouldBeNull();
            await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var recovery = new AgentTaskLandNotificationService(restarted, h.Queue, new CompletionNoteFlushQueue(),
                h.Runtime, TimeProvider.System);
            await recovery.ReconcileAsync(note.Id, CancellationToken.None);
            var saved = await restarted.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
            saved.QueueMessageId.ShouldNotBeNull();
            (await restarted.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(1);
            if (busy) h.Adapter.Inputs.ShouldBeEmpty();
            else
            {
                await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
                h.Adapter.SubmittedBodies.ShouldContain(saved.Body);
            }
            return;
        }

        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        if (cut == "lost-wakeup")
        {
            if (busy) h.Adapter.Inputs.ShouldBeEmpty();
            await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var recovery = new AgentTaskLandNotificationService(restarted, h.Queue, new CompletionNoteFlushQueue(),
                h.Runtime, TimeProvider.System, new DroppedWakeupBoundary());
            await recovery.ReconcileAsync(note.Id, CancellationToken.None);
            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            h.Adapter.SubmittedBodies.ShouldContain(queued.Body);
            return;
        }

        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 10;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10,
            Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        if (cut is "receipt-before-save" or "after-receipt")
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11,
                Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        h.Runner.SetTranscript(new(h.SessionId, [new SessionRunnerTranscriptEvent(h.SessionId, 11, TranscriptKinds.UserPrompt,
            "c478-land-" + note.Id.ToString("N"), null, DateTimeOffset.UtcNow, "user", queued.Body.Replace("\n", ""),
            null, null, null, null, null)], 11));
        if (cut == "receipt-before-save")
            await Should.ThrowAsync<IOException>(() => service.ReconcileAsync(note.Id, CancellationToken.None));
        await using var recovered = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var confirm = new AgentTaskLandNotificationService(recovered, h.Queue, new CompletionNoteFlushQueue(),
            h.Runtime, TimeProvider.System);
        await confirm.ReconcileAsync(note.Id, CancellationToken.None);
        await confirm.ReconcileAsync(note.Id, CancellationToken.None);
        var final = await recovered.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        final.State.ShouldBe(LandNotificationState.Confirmed);
        (await recovered.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt))
            .ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty();
        if (busy) h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    private static async Task QueueCrashAsync(string producer, string cut, bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var sha = new string('a', 40);
        var body = producer == "worker"
            ? $"SourceLanding {operation:D}\nL={sha}\n{DelegationReportFormatter.TaskMarker(taskId)}"
            : "Mutation complete.\n--- next stage ---\nnext: none\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: taskId, deliverIfIdle: false);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.SourceTaskId == taskId);
        queued.Body.ShouldContain(producer == "worker" ? operation.ToString("D") : "next: none");
        if (cut == "before-submit")
        {
            if (busy) h.Adapter.Inputs.ShouldBeEmpty();
            else
            {
                await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
                h.Adapter.SubmittedBodies.ShouldContain(queued.Body);
            }
            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            return;
        }

        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 4;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 4,
            Kind = TranscriptKinds.AssistantText, Text = producer + " baseline", CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        if (cut is "after-prompt" or "after-receipt")
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 5,
                Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        var typed = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await db.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt))
            .ShouldBe(cut is "after-prompt" or "after-receipt" ? 1 : 0);
        h.Adapter.Inputs.Count.ShouldBe(typed);
        if (busy || cut is "after-prompt" or "after-receipt") h.Adapter.Inputs.ShouldBeEmpty();
    }

    private static async Task LandWrongSessionAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        var other = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = other, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = other, Sequence = 11, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    private static async Task LandWrongIdentityAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body.Replace(note.Id.ToString("N"), Guid.NewGuid().ToString("N")),
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
    }

    private static async Task LandPartialPromptAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body[..200] + queued.Body[^100..], CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
    }

    private static async Task LandStalePromptAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
    }

    private sealed class DeliveryCut(string cut) : LandDeliveryBoundary
    {
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            (cut, boundary) switch
            {
                ("before-enqueue", "before-enqueue") => Task.FromException(new IOException("owned enqueue failure")),
                ("queue-inserted", "queue-inserted") => Task.FromException(new IOException("owned queue-ack failure")),
                ("receipt-before-save", "receipt-before-save") => Task.FromException(new IOException("owned receipt save failure")),
                _ => Task.CompletedTask,
            };
    }

    private sealed class DroppedWakeupBoundary : LandDeliveryBoundary
    {
        public override bool DropWakeup(string boundary, Guid identity) => true;
    }
}

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
}

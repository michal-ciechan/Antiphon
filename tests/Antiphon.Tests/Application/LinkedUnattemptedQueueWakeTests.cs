using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// C488: a hosted reconcile is the second recovery path for a wakeup lost after the
/// queue row linked. At the production base this is red because that row does not
/// wake the flush. A claimed attempt, a future hold and a dropped completion wakeup
/// stay unwoken.
/// </summary>
[Category("Integration")]
public sealed class LinkedUnattemptedQueueWakeTests
{
    [Timeout(180_000)]
    [Test]
    [Arguments("linked-pending")]
    [Arguments("one-attempt")]
    [Arguments("held")]
    [Arguments("drop-wakeup")]
    public async Task C488_ReconcileWakesOnlyALinkedPendingRowWithZeroAttempts(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, harness.SessionId);
        var now = DateTime.UtcNow;
        var attempts = shape == "one-attempt" ? 1 : 0;
        var status = shape == "one-attempt" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending;
        var queueId = Guid.NewGuid();
        note.Kind = LandNotificationKind.TaskCompletion;
        note.State = LandNotificationState.AwaitingReceipt;
        note.CompletionSnapshotJson = "{}";
        note.CreatedAt = now;
        note.NextAttemptAt = now;
        note.QueueMessageId = queueId;
        note.EnqueuedAt = now;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = queueId,
            AgentSessionId = harness.SessionId,
            Body = note.Body,
            Status = status,
            Origin = QueuedMessageOrigin.Delegation,
            SourceTaskId = note.TaskId,
            SourceLandNotificationId = note.Id,
            ContentDigest = note.ContentDigest,
            CreatedAt = now,
            DeliveryAttempts = attempts,
            HoldUntil = shape == "held" ? now.AddHours(1) : null,
            Sequence = 1,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var flushes = new RecordingFlush();
        var service = new AgentTaskLandNotificationService(
            db, harness.Queue, flushes, harness.Runtime, TimeProvider.System,
            shape == "drop-wakeup" ? new DropCompletionWakeup() : null);
        await service.ReconcileAsync(note.Id, CancellationToken.None);

        var saved = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queueId);
        saved.DeliveryAttempts.ShouldBe(attempts, shape + " must not retype a claimed or unattempted row");
        saved.Status.ShouldBe(status);
        if (shape == "linked-pending")
            flushes.Sessions.ShouldHaveSingleItem(shape + ": the flush is not woken").ShouldBe(harness.SessionId,
                "linked, Pending, zero attempts: the flush is not woken");
        else
            flushes.Sessions.ShouldBeEmpty(shape + ": the flush is not woken");
    }

    private sealed class RecordingFlush : CompletionNoteFlushQueue
    {
        public List<Guid> Sessions { get; } = new();

        public override bool TryEnqueue(Guid sessionId)
        {
            Sessions.Add(sessionId);
            return base.TryEnqueue(sessionId);
        }
    }

    private sealed class DropCompletionWakeup : LandDeliveryBoundary
    {
        public override bool DropWakeup(string boundary, Guid identity) => boundary == "completion";
    }
}

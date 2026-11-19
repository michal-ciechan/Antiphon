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
/// C488: a hosted reconcile wakes a linked Pending row that has never been attempted.
/// A Pending row that already has one attempt, a Sent row, a future hold and a dropped
/// completion wakeup stay unwoken. A row linked in this pass is woken once, at the
/// relink site. The recording queue is drained so the row's attempt and status are
/// what the flush actually wrote.
/// </summary>
[Category("Integration")]
public sealed class LinkedUnattemptedQueueWakeTests
{
    [Timeout(180_000)]
    [Test]
    [Arguments("linked-pending")]
    [Arguments("pending-attempt")]
    [Arguments("same-pass")]
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
        var attempts = shape is "one-attempt" or "pending-attempt" ? 1 : 0;
        var status = shape == "one-attempt" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending;
        var linkedThisPass = shape == "same-pass";
        var queueId = Guid.NewGuid();
        note.Kind = LandNotificationKind.TaskCompletion;
        note.State = LandNotificationState.AwaitingReceipt;
        note.CompletionSnapshotJson = "{}";
        note.CreatedAt = now;
        note.NextAttemptAt = now;
        if (!linkedThisPass)
        {
            note.QueueMessageId = queueId;
            note.EnqueuedAt = now;
        }
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
            // A same-generation claim can stop in the composer check and leave the row
            // untouched. An older start makes a wrongful wake retype, so the attempt
            // assertion can fail.
            LastDeliveryStartedAt = shape == "pending-attempt" ? now.AddHours(-2) : null,
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

        var wakes = flushes.Sessions.ToList();
        await DrainAsync(flushes, harness);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var saved = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queueId);
        var expectWake = shape is "linked-pending" or "same-pass";
        if (expectWake)
        {
            saved.Status.ShouldBe(QueuedMessageStatus.Sent, shape);
            saved.DeliveryAttempts.ShouldBe(1, shape);
            wakes.ShouldHaveSingleItem(shape + ": woken once").ShouldBe(harness.SessionId, shape);
        }
        else
        {
            saved.Status.ShouldBe(status, shape);
            saved.DeliveryAttempts.ShouldBe(attempts, shape);
            wakes.ShouldBeEmpty(shape + ": the flush is not woken");
        }
    }

    private static async Task DrainAsync(RecordingFlush flushes, BridgeQueueHarness harness)
    {
        var seen = new HashSet<Guid>();
        foreach (var sessionId in flushes.Sessions.ToList())
        {
            if (!seen.Add(sessionId))
                continue;
            await harness.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
            flushes.Complete(sessionId);
        }
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

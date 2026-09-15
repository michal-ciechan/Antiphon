using System.Data.Common;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DispatchBaseNotificationTests
{
    [Test]
    public async Task C540_CollapsedProjectionIsAtomic()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var dispatch = await SeedDispatchEventAsync(db, task.Id, DateTime.UtcNow);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(task, dispatch,
            [new(DispatchBaseNotificationPayload.SiblingKey(Guid.NewGuid()),
                "Kept representative. At the observed tips, this warning also covers 2 other kept sibling branches.")], default);
        await db.SaveChangesAsync();
        var original = await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0]);
        var failure = new ProjectionCommitFailure(original.NotificationId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(schema.ConnectionString).AddInterceptors(failure).Options;
        await using (var projector = new AppDbContext(options))
            await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(original.Id, default);
        failure.Fired.ShouldBeTrue();
        await using (var check = CreateContext(schema))
        {
            (await check.AgentTaskEvents.CountAsync(e => e.Id == original.Id)).ShouldBe(0);
            (await check.AgentTaskLandNotifications.CountAsync(n => n.Id == original.NotificationId)).ShouldBe(0);
            (await check.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == original.Id)).MaterializedAt.ShouldBeNull();
        }
        for (var pass = 0; pass < 2; pass++)
        {
            await using var projector = CreateContext(schema);
            await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(original.Id, default);
        }
        await using var final = CreateContext(schema);
        (await final.AgentTaskEvents.CountAsync(e => e.Id == original.Id)).ShouldBe(1);
        var note = (await final.AgentTaskLandNotifications.Where(n => n.Id == original.NotificationId).ToListAsync()).ShouldHaveSingleItem();
        note.Body.ShouldBe(original.Body); note.SourceEventId.ShouldBe(original.Id);
        (await final.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == original.Id)).MaterializedAt.ShouldNotBeNull();
    }

    [Test]
    public Task C540_WarningRequiresCompletePrompt() => CheckReceiptAsync(truncated: true, sequenceFloor: true);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task C540_WarningRequiresAttemptFloor(bool sequenceFloor) => CheckReceiptAsync(truncated: false, sequenceFloor);

    private static async Task CheckReceiptAsync(bool truncated, bool sequenceFloor)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var parent = Guid.NewGuid(); await SeedParentSessionAsync(db, parent); await db.SaveChangesAsync();
        var task = await SeedBareTaskAsync(db, parent);
        var now = DateTime.UtcNow;
        var dispatch = await SeedDispatchEventAsync(db, task.Id, now);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(task, dispatch,
            [new(DispatchBaseNotificationPayload.SiblingKey(Guid.NewGuid()),
                "representative " + new string('x', 700) + " immutable covered branches tail")], default);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await new DispatchBaseWarningIntentService(db, TimeProvider.System).MaterializeAsync(ids[0], default);
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == ids[0]);
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = parent, SourceTaskId = task.Id, SourceLandNotificationId = note.Id,
            Body = note.Body, CreatedAt = now, Status = QueuedMessageStatus.Sent, DeliveryAttempts = 1,
            LastDeliveryBaselineSequence = sequenceFloor ? 10 : null, LastDeliveryStartedAt = now,
        };
        db.SessionQueuedMessages.Add(row); note.QueueMessageId = row.Id; note.State = LandNotificationState.AwaitingReceipt;
        var negative = truncated ? note.Body[..300] : note.Body;
        if (truncated)
        {
            PromptSubmissionMatch.IsConfirmedBy(note.Body, negative).ShouldBeTrue();
            PromptSubmissionMatch.IsCompleteIn(note.Body, negative).ShouldBeFalse();
        }
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = parent, Kind = TranscriptKinds.UserPrompt, Text = negative,
            Sequence = truncated ? 11 : 9, Timestamp = truncated ? now : now.AddMinutes(-5), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        await using var provider = CreateProvider(schema.ConnectionString, Path.GetTempPath(), "master");
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(note.Id, default);
        db.ChangeTracker.Clear();
        (await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).ConfirmedAt.ShouldBeNull();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = parent, Kind = TranscriptKinds.UserPrompt, Text = note.Body,
            Sequence = 12, Timestamp = now.AddSeconds(1), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(note.Id, default);
        db.ChangeTracker.Clear();
        var confirmed = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
        confirmed.ConfirmedAt.ShouldNotBeNull(); confirmed.ConfirmingPromptSequence.ShouldBe(12);
    }

    private sealed class ProjectionCommitFailure(Guid noteId) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            if (!Fired && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTaskLandNotification>().Any(e => e.Entity.Id == noteId))
            { Fired = true; throw new IOException("owned projection failure after SaveChanges"); }
            return ValueTask.FromResult(result);
        }
    }
}

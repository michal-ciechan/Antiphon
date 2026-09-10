using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandNotificationRecoveryTests
{
    [Test]
    public async Task C467_V09_KeyedQueueRacesAndDistinctEvents()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var first = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var second = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var notification = Guid.NewGuid();
        var task = Guid.NewGuid();
        Guid firstId = default, secondId = default;
        await Task.WhenAll(
            first.Queue.EnqueueAsync(first.SessionId, "immutable keyed body", MessageSendMode.WhenIdle, CancellationToken.None,
                QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false,
                sourceLandNotificationId: notification, onCreated: id => firstId = id),
            second.Queue.EnqueueAsync(first.SessionId, "immutable keyed body", MessageSendMode.WhenIdle, CancellationToken.None,
                QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false,
                sourceLandNotificationId: notification, onCreated: id => secondId = id));
        firstId.ShouldNotBe(Guid.Empty);
        firstId.ShouldBe(secondId);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == notification)).ShouldBe(1);
        await Should.ThrowAsync<ConflictException>(() => first.Queue.EnqueueAsync(second.SessionId, "immutable keyed body", MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false, sourceLandNotificationId: notification));
        await first.Queue.EnqueueAsync(first.SessionId, "immutable keyed body", MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false, sourceLandNotificationId: Guid.NewGuid());
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task)).ShouldBe(2);
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = first.SessionId,
            SourceLandNotificationId = notification, Body = "duplicate", CreatedAt = DateTime.UtcNow });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        first.Adapter.Inputs.ShouldBeEmpty();
        second.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(1, 5)] [Arguments(2, 10)] [Arguments(3, 20)] [Arguments(4, 40)]
    [Arguments(5, 80)] [Arguments(6, 160)] [Arguments(7, 300)] [Arguments(8, 300)]
    public async Task C467_V08_RetryAndDestinationMatrix(int attempt, int seconds)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var boundary = new FailedInsert();
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, clock, boundary);
        for (var i = 0; i < attempt; i++)
        {
            await service.ReconcileAsync(note.Id, CancellationToken.None);
            await db.Entry(note).ReloadAsync();
            if (i + 1 < attempt) clock.SetUtcNow(note.NextAttemptAt);
        }
        note.EnqueueAttempts.ShouldBe(attempt);
        note.NextAttemptAt.ShouldBe(clock.GetUtcNow().UtcDateTime.AddSeconds(seconds));
        note.State.ShouldBe(LandNotificationState.RetryPending); note.ConfirmedAt.ShouldBeNull();
        note.LastErrorCode.ShouldContain("IOException"); note.QueueMessageId.ShouldBeNull();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        boundary.Calls.ShouldBe(attempt, "not-yet-due retry does not attempt enqueue");
        clock.SetUtcNow(note.NextAttemptAt);
        await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, clock).ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync(); note.QueueMessageId.ShouldNotBeNull();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.SourceLandNotificationId.ShouldBe(note.Id); row.SourceTaskId.ShouldBe(note.TaskId);
        row.ContentDigest.ShouldBe(note.ContentDigest); row.NoteHeader.ShouldBe(note.Body.Split('\n')[0]);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    private sealed class FailedInsert : LandDeliveryBoundary
    {
        public int Calls;
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        { Calls++; throw new IOException("owned insertion failure"); }
    }
}

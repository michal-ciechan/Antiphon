using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Orchestration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandNotificationRecoveryTests
{
    [Test]
    [Arguments("none")]
    [Arguments("missing")]
    [Arguments("stopped")]
    [Arguments("failed")]
    [Arguments("deleted")]
    [Arguments("destination-edit")]
    public async Task C467_V08_DestinationSnapshotsRemainOwed(string state)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var destination = state == "missing" ? Guid.NewGuid() : h.SessionId;
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, destination, state == "none" ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session);
        if (state is "stopped" or "failed") await db.AgentSessions.Where(s => s.Id == destination).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, state == "stopped" ? SessionStatus.Stopped : SessionStatus.Failed));
        if (state == "deleted") await db.AgentSessions.Where(s => s.Id == destination).ExecuteDeleteAsync();
        if (state == "destination-edit") await db.AgentTasks.Where(t => t.Id == note.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.ParentSessionId, (Guid?)Guid.NewGuid()));
        await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System).ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ParentSessionId.ShouldBe(destination); note.ConfirmedAt.ShouldBeNull();
        note.State.ShouldBe(state == "none" ? LandNotificationState.NotRequired : state == "destination-edit" ? LandNotificationState.AwaitingReceipt : LandNotificationState.DestinationUnavailable);
        if (state == "destination-edit") (await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId)).AgentSessionId.ShouldBe(destination);
        else note.QueueMessageId.ShouldBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C467_V10_BootScanFairnessAndClearedPending()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString,
            ConfigureServices = services => { services.AddSingleton<CompletionNoteFlushQueue>(); services.AddScoped<AgentTaskLandNotificationService>(); } });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var notes = new List<AgentTaskLandNotification>();
        for (var i = 0; i < 263; i++) notes.Add(await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId));
        var ordered = notes.OrderBy(n => n.Id).ToArray();
        ordered[0].ParentSessionId = null; // Poison head, whose missing destination must not starve later pages.
        ordered[50].NextAttemptAt = DateTime.UtcNow.AddHours(1);
        ordered[140].State = LandNotificationState.Confirmed; ordered[140].ConfirmedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var expected = notes.Where(n => n.Id != ordered[0].Id && n.Id != ordered[50].Id && n.Id != ordered[140].Id).Select(n => n.Id).ToArray();
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && await db.AgentTaskLandNotifications.CountAsync(n => expected.Contains(n.Id) && n.QueueMessageId != null) < expected.Length)
                await Task.Delay(100, stop.Token);
            (await db.AgentTaskLandNotifications.CountAsync(n => expected.Contains(n.Id) && n.QueueMessageId != null)).ShouldBe(expected.Length);
            (await db.AgentTasks.CountAsync(t => t.LandRequestedAt != null)).ShouldBe(0);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId != null)).ShouldBe(expected.Length);
        }
        finally { await worker.StopAsync(CancellationToken.None); worker.Dispose(); }
        h.Adapter.Inputs.ShouldBeEmpty("this test proves hosted handoff, not caller receipt");
    }

    [Test]
    public async Task C467_V14_LandNotesDoNotCountAsReports()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System).ReconcileAsync(note.Id, CancellationToken.None);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.ConversationKey = $"task:{note.TaskId:N}"; row.HoldUntil = DateTime.UtcNow.AddHours(1);
        // Adversarial digest equality ensures the Land exclusion, rather than a digest mismatch, protects the row.
        row.ContentDigest = DelegationNoteDigest.Compute("ordinary report");
        var body = row.Body; var digest = row.ContentDigest; var hold = row.HoldUntil;
        (await db.AgentTasks.SingleAsync(t => t.Id == note.TaskId)).Result = "ordinary report";
        await db.SaveChangesAsync();
        (await h.Queue.TryApplyDistillationAsync(new DistillRequest(note.TaskId, row.Id, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1), OutputDistillerMode.Apply), digest!, "replacement summary", CancellationToken.None)).ShouldBe("identity");
        (await AgentTaskCheckService.HasCompletionNoteAsync(db, h.SessionId, note.TaskId, CancellationToken.None)).ShouldBeFalse();
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance), Options.Create(new DelegationSettings()),
            new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance);
        await tasks.GetAsync(note.TaskId, CancellationToken.None, h.SessionId); await tasks.MarkReadAsync(note.TaskId, CancellationToken.None);
        await new OutputDistillationService(db, null!, Options.Create(new DelegationSettings()), TimeProvider.System, NullLogger<OutputDistillationService>.Instance)
            .ReleaseHoldAsync(row.Id, CancellationToken.None);
        await db.Entry(row).ReloadAsync(); await db.Entry(note).ReloadAsync();
        row.Body.ShouldBe(body); row.ContentDigest.ShouldBe(digest); row.HoldUntil.ShouldBe(hold); note.ConfirmedAt.ShouldBeNull();
        row.HoldUntil = null; await db.SaveChangesAsync();
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await db.Entry(row).ReloadAsync(); row.Body.ShouldBe(body);
        h.Adapter.SubmittedBodies.ShouldContain(body);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == note.TaskId && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBeFalse();
        await h.Queue.EnqueueAsync(h.SessionId, "ordinary report", MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, $"task:{note.TaskId:N}", note.TaskId, DelegationNoteDigest.Compute("ordinary report"), deliverIfIdle: false);
        (await AgentTaskCheckService.HasCompletionNoteAsync(db, h.SessionId, note.TaskId, CancellationToken.None)).ShouldBeTrue();
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == note.TaskId)).ShouldBe(2);
    }
    [Test]
    public async Task C467_V09_KeyedQueueRacesAndDistinctEvents()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var first = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var second = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var notification = Guid.NewGuid();
        var task = Guid.NewGuid();
        await first.Queue.EnqueueAsync(first.SessionId, "ordinary adversarial report", MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false);
        var (firstId, secondId) = await LandQueueRaceWorker.RunPairAsync(schema.ConnectionString, first.SessionId, task, notification);
        firstId.ShouldNotBe(Guid.Empty);
        firstId.ShouldBe(secondId);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == notification)).ShouldBe(1);
        await Should.ThrowAsync<ConflictException>(() => first.Queue.EnqueueAsync(second.SessionId, "immutable keyed body", MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false, sourceLandNotificationId: notification));
        await first.Queue.EnqueueAsync(first.SessionId, "immutable keyed body", MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Delegation, sourceTaskId: task, contentDigest: "digest", deliverIfIdle: false, sourceLandNotificationId: Guid.NewGuid());
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task)).ShouldBe(3);
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

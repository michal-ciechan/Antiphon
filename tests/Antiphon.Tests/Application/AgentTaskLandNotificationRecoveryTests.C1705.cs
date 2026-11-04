using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskLandNotificationRecoveryTests
{
    /// <summary>
    /// CARD-1705: one caller's held queue lock (a delivery stuck before typing) must not stall the
    /// hosted notification scan for every other caller. The busy caller's notices stay owed with no
    /// queue row and no failure bookkeeping, and the next scan enqueues them once the lock is free.
    /// </summary>
    [Test]
    public async Task C1705_HeldQueueLockDoesNotStallOtherCallersNotices()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanSignal();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
                services.AddSingleton<LandDeliveryBoundary>(scans);
            } });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var busy = h.SessionId;
        var free = Guid.NewGuid();
        var clone = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == busy);
        clone.Id = free;
        clone.DelegationTokenHash = null;
        db.AgentSessions.Add(clone);
        await db.SaveChangesAsync();

        // The scan walks notes in database id order; the held caller's notice must come first so the
        // other caller's notice is the one queued behind it.
        var busyNotes = new List<Guid> { (await AgentTaskLandReceiptTests.SeedAsync(db, busy)).Id };
        var freeNote = (await AgentTaskLandReceiptTests.SeedAsync(db, free)).Id;
        while ((await db.AgentTaskLandNotifications.AsNoTracking().OrderBy(n => n.Id).Select(n => n.Id).FirstAsync()) == freeNote)
            busyNotes.Add((await AgentTaskLandReceiptTests.SeedAsync(db, busy)).Id);

        var queueLock = h.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(busy);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = false;
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, TimeSpan.FromSeconds(10)))
                .ShouldBeTrue("one held caller lock must not keep the notification scan from returning");
            var queued = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == freeNote);
            queued.QueueMessageId.ShouldNotBeNull("the free caller's notice is enqueued within the same scan");
            queued.State.ShouldBe(LandNotificationState.AwaitingReceipt);
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.QueueMessageId)).AgentSessionId.ShouldBe(free);
            foreach (var id in busyNotes)
            {
                var owed = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
                owed.QueueMessageId.ShouldBeNull();
                owed.State.ShouldBe(LandNotificationState.Queued);
                owed.EnqueueAttempts.ShouldBe(0, "a busy lock is not a failed attempt");
                owed.LastErrorCode.ShouldBeNull();
            }
            (await db.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.AgentSessionId == busy)).ShouldBe(0);

            var seen = scans.Count;
            queueLock.Release();
            released = true;
            (await scans.WaitForScanAsync(seen + 1, TimeSpan.FromSeconds(30))).ShouldBeTrue("the next scan runs after the lock is free");
            foreach (var id in busyNotes)
                (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == id)).QueueMessageId
                    .ShouldNotBeNull("the next scan retries the held caller's notice");
            (await db.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.AgentSessionId == busy)).ShouldBe(busyNotes.Count);
        }
        finally
        {
            if (!released) queueLock.Release();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
        h.Adapter.Inputs.ShouldBeEmpty("this test proves hosted handoff, not caller receipt");
    }

    private sealed class ScanSignal : LandDeliveryBoundary
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "notification-scan") Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }

        public async Task<bool> WaitForScanAsync(int count, TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (Count < count)
            {
                if (DateTime.UtcNow >= deadline) return false;
                await Task.Delay(50);
            }
            return true;
        }
    }
}

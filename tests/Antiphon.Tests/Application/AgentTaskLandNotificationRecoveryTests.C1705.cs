using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
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

    /// <summary>
    /// CARD-1705 r1 D-1: the same held caller lock, met first in the scan's legacy-captured phase.
    /// A due Captured publication for the held caller is produced inline; its enqueue must not wait
    /// on that lock without limit, or the land-notification phase never runs for anyone else.
    /// </summary>
    [Test]
    public async Task C1705_HeldQueueLockDoesNotStallTheCapturedPhase()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanSignal();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
                services.AddScoped<LegacyCheckNotePublicationService>();
                services.AddSingleton<LandDeliveryBoundary>(scans);
            } });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var busy = h.SessionId;
        var free = await CloneSessionAsync(db, busy);
        var publication = await SeedDueCapturedAsync(db, h, busy);
        var freeNote = (await AgentTaskLandReceiptTests.SeedAsync(db, free)).Id;

        var queueLock = h.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(busy);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = false;
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, TimeSpan.FromSeconds(10)))
                .ShouldBeTrue("a held caller lock met in the captured phase must not keep the scan from returning");
            (await db.LegacyCheckNotePublications.AsNoTracking().SingleAsync(p => p.Id == publication.Id)).State
                .ShouldBe(LegacyCheckNoteState.Produced, "the captured phase still produces the held caller's note");
            var queued = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == freeNote);
            queued.QueueMessageId.ShouldNotBeNull("the free caller's notice is enqueued within the same scan");
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.QueueMessageId)).AgentSessionId.ShouldBe(free);
            var owed = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == publication.NotificationId);
            owed.QueueMessageId.ShouldBeNull();
            owed.State.ShouldBe(LandNotificationState.Queued);
            owed.EnqueueAttempts.ShouldBe(0, "a busy lock is not a failed attempt");
            owed.LastErrorCode.ShouldBeNull();
            (await db.SessionQueuedMessages.AsNoTracking().CountAsync(m => m.AgentSessionId == busy)).ShouldBe(0);

            var seen = scans.Count;
            queueLock.Release();
            released = true;
            (await scans.WaitForScanAsync(seen + 1, TimeSpan.FromSeconds(30))).ShouldBeTrue("the next scan runs after the lock is free");
            var delivered = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == publication.NotificationId);
            delivered.QueueMessageId.ShouldNotBeNull("the produced note is not lost: the next scan enqueues it");
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == delivered.QueueMessageId)).AgentSessionId.ShouldBe(busy);
        }
        finally
        {
            if (!released) queueLock.Release();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    /// <summary>
    /// CARD-1705 r1 D-2: once one of a caller's notices times out on its lock, the caller's later
    /// notices in the same scan are skipped even if the lock frees meanwhile, so no later notice
    /// jumps ahead of the one left for the next scan.
    /// </summary>
    [Test]
    public async Task C1705_BusyCallerKeepsNoticeOrderWhenItsLockFreesMidScan()
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
        var free = await CloneSessionAsync(db, busy);

        // The scan walks notes in database id order: A1 < B < A2, where A is the held caller. The ids
        // are fixed, not drawn: random ids left A1 or A2 missing about once in 65 runs.
        var ids = OrderedIds(3);
        var busyNotes = new List<Guid> { (await AgentTaskLandReceiptTests.SeedAsync(db, busy, id: ids[0])).Id };
        var freeNote = (await AgentTaskLandReceiptTests.SeedAsync(db, free, id: ids[1])).Id;
        busyNotes.Add((await AgentTaskLandReceiptTests.SeedAsync(db, busy, id: ids[2])).Id);
        var ordered = await db.AgentTaskLandNotifications.AsNoTracking().OrderBy(n => n.Id).Select(n => n.Id).ToListAsync();
        var later = ordered.SkipWhile(id => id != freeNote).Skip(1).ToList();
        var earlier = ordered.TakeWhile(id => id != freeNote).ToList();
        earlier.ShouldNotBeEmpty("A1 precedes B");
        later.ShouldNotBeEmpty("A2 follows B");

        var queueLock = h.Provider.GetRequiredService<SessionMessageQueueService>().GetLock(busy);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        // A1 has already timed out when B reaches its enqueue; A's lock frees there, before A2.
        var released = 0;
        scans.BeforeEnqueue = id =>
        {
            if (id == freeNote && Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        };
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, TimeSpan.FromSeconds(20))).ShouldBeTrue("the first scan returns");
            Volatile.Read(ref released).ShouldBe(1, "B reached its enqueue and freed A's lock mid-scan");
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == freeNote)).QueueMessageId
                .ShouldNotBeNull("B is enqueued in the first scan");
            foreach (var id in earlier.Concat(later))
                (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == id)).QueueMessageId
                    .ShouldBeNull("no A notice is enqueued in the scan where A1 timed out, even after A's lock frees");

            (await scans.WaitForScanAsync(2, TimeSpan.FromSeconds(30))).ShouldBeTrue("the next scan runs");
            var rows = await db.AgentTaskLandNotifications.AsNoTracking()
                .Where(n => busyNotes.Contains(n.Id)).Select(n => new { n.Id, n.QueueMessageId }).ToListAsync();
            rows.ShouldAllBe(n => n.QueueMessageId != null);
            var sequences = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == busy).ToDictionaryAsync(m => m.Id, m => m.Sequence);
            var queueOrder = rows.OrderBy(n => sequences[n.QueueMessageId!.Value]).Select(n => n.Id).ToList();
            queueOrder.ShouldBe(earlier.Concat(later).ToList(), "A's queue rows keep the notices' order: A1 before A2");
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    /// <summary>
    /// CARD-1705 M13: the busy skip covers every later notice of the caller in the scan, not only the
    /// next one. Exactly one A notice precedes B and two follow it (A1 &lt; B &lt; A2 &lt; A3); A's lock frees
    /// at B's enqueue. A skip that cleared A from the busy set would skip A2 and then enqueue A3 ahead
    /// of A1 and A2.
    /// </summary>
    [Test]
    public async Task C1705_BusySkipCoversEveryLaterNoticeOfTheCaller()
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
        var free = await CloneSessionAsync(db, busy);

        var ids = OrderedIds(4);
        var a1 = (await AgentTaskLandReceiptTests.SeedAsync(db, busy, id: ids[0])).Id;
        var freeNote = (await AgentTaskLandReceiptTests.SeedAsync(db, free, id: ids[1])).Id;
        var a2 = (await AgentTaskLandReceiptTests.SeedAsync(db, busy, id: ids[2])).Id;
        var a3 = (await AgentTaskLandReceiptTests.SeedAsync(db, busy, id: ids[3])).Id;
        var busyNotes = new List<Guid> { a1, a2, a3 };
        (await db.AgentTaskLandNotifications.AsNoTracking().OrderBy(n => n.Id).Select(n => n.Id).ToListAsync())
            .ShouldBe(new List<Guid> { a1, freeNote, a2, a3 }, "the scan meets A1, B, A2, A3 in that order");

        var queueLock = h.Queue.GetLock(busy);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = 0;
        scans.BeforeEnqueue = id =>
        {
            if (id == freeNote && Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        };
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, TimeSpan.FromSeconds(20))).ShouldBeTrue("the first scan returns");
            Volatile.Read(ref released).ShouldBe(1, "B reached its enqueue and freed A's lock mid-scan");
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == freeNote)).QueueMessageId
                .ShouldNotBeNull("B is enqueued in the first scan");
            foreach (var (id, name) in new[] { (a1, "A1"), (a2, "A2"), (a3, "A3") })
                (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == id)).QueueMessageId
                    .ShouldBeNull($"{name} is not enqueued in the scan where A1 timed out, even after A's lock frees");

            (await scans.WaitForScanAsync(2, TimeSpan.FromSeconds(30))).ShouldBeTrue("the next scan runs");
            var rows = await db.AgentTaskLandNotifications.AsNoTracking()
                .Where(n => busyNotes.Contains(n.Id)).Select(n => new { n.Id, n.QueueMessageId }).ToListAsync();
            rows.ShouldAllBe(n => n.QueueMessageId != null);
            var sequences = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == busy).ToDictionaryAsync(m => m.Id, m => m.Sequence);
            rows.OrderBy(n => sequences[n.QueueMessageId!.Value]).Select(n => n.Id).ToList()
                .ShouldBe(busyNotes, "A's queue rows keep the notices' order: A1, A2, A3");
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    /// <summary>
    /// CARD-1705 M8: the captured and land-notification phases share one busy set per pass. A caller
    /// whose lock timed out on its produced note in the captured phase is skipped in the land phase of
    /// the same pass without a second wait, so a held caller costs one bounded wait per pass, not two.
    /// Each before-enqueue boundary of a busy caller's notice precedes exactly one bounded wait.
    /// </summary>
    [Test]
    public async Task C1705_CapturedPhaseTimeoutSkipsTheCallersLandNoticesInTheSamePass()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scans = new ScanSignal();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
                services.AddScoped<LegacyCheckNotePublicationService>();
                services.AddSingleton<LandDeliveryBoundary>(scans);
            } });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var busy = h.SessionId;
        var free = await CloneSessionAsync(db, busy);
        var busyLandNote = (await AgentTaskLandReceiptTests.SeedAsync(db, busy)).Id;
        var publication = await SeedDueCapturedAsync(db, h, busy);
        var freeNote = (await AgentTaskLandReceiptTests.SeedAsync(db, free)).Id;

        var queueLock = h.Queue.GetLock(busy);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = false;
        var worker = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        try
        {
            (await scans.WaitForScanAsync(1, TimeSpan.FromSeconds(10))).ShouldBeTrue("the first scan returns");
            var firstPass = scans.EnqueueHits.Where(hit => hit.Scan == 0).Select(hit => hit.Id).ToList();
            (await db.LegacyCheckNotePublications.AsNoTracking().SingleAsync(p => p.Id == publication.Id)).State
                .ShouldBe(LegacyCheckNoteState.Produced, "the captured phase produced A's note and met A's held lock");
            firstPass.Count(id => id == freeNote).ShouldBe(1, "the free caller's notice reaches its enqueue in the first pass");
            firstPass.Count(id => id == publication.NotificationId)
                .ShouldBe(1, "A's produced note waits once, in the captured phase");
            firstPass.Count(id => id == busyLandNote)
                .ShouldBe(0, "A's land notice is skipped in the land phase of the same pass, with no second wait");
            (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == busyLandNote)).QueueMessageId.ShouldBeNull();

            var seen = scans.Count;
            queueLock.Release();
            released = true;
            (await scans.WaitForScanAsync(seen + 1, TimeSpan.FromSeconds(30))).ShouldBeTrue("the next scan runs after the lock is free");
            foreach (var id in new[] { busyLandNote, publication.NotificationId })
                (await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == id)).QueueMessageId
                    .ShouldNotBeNull("the next scan enqueues A's notices once the lock is free");
        }
        finally
        {
            if (!released) queueLock.Release();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    /// <summary>
    /// CARD-1705 M14: only the hosted scan bounds the lock wait. A producer publishing a Check note
    /// for a caller whose queue lock is held waits for that lock, so the inline handoff is complete
    /// when the publish returns.
    /// </summary>
    [Test]
    public async Task C1705_ProducerWaitsForAHeldQueueLock()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        var generation = SessionGeneration.Normalize(session.StartedAt);
        var now = DateTime.UtcNow;
        var checkedId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId, PhysicalAgentId = h.AgentId, SessionId = h.SessionId, AcceptedStartedAt = generation,
            BoundaryIdentity = "boundary-1", BoundaryCreatedAt = now, ContinuationCreatedAt = now,
            ConfiguredThresholdMinutes = 10, DetectedAt = now, State = CheckCompactionRecoveryState.AwaitingCheck,
            ResumeSessionId = h.SessionId, ResumeAcceptedStartedAt = generation,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkedId, RootTaskId = checkedId, Title = "subject", Goal = "watch", Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working, AgentId = h.AgentId,
            AgentSessionId = h.SessionId, ParentSessionId = h.SessionId, ReplyTo = AgentTaskReplyTo.Session,
            WorkingDirectory = h.TempRoot, CreatedAt = now, DispatchedAt = now, Attempt = 1, CheckCount = 1,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = runId, RootTaskId = runId, Title = "read", Goal = "interpret", Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded, AgentId = h.AgentId,
            AgentSessionId = h.SessionId, WorkingDirectory = h.TempRoot, CreatedAt = now, Result = "useful",
        });
        await db.SaveChangesAsync();
        var publisher = new LegacyCheckNotePublicationService(db, TimeProvider.System,
            new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System));
        var subject = await db.AgentTasks.SingleAsync(t => t.Id == checkedId);

        var queueLock = h.Queue.GetLock(h.SessionId);
        (await queueLock.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue("the test owns the held delivery's lock");
        var released = false;
        try
        {
            var publish = publisher.TryPublishAsync(
                subject, 1, "Check note behind a held lock", "event", runId, false, null, CancellationToken.None, episodeId);
            // Longer than the scan's bound: a producer given that bound returns busy before this.
            var held = TimeSpan.FromMilliseconds(ScanQueueLockWaitMs + 500);
            (await Task.WhenAny(publish, Task.Delay(held)) == publish)
                .ShouldBeFalse($"the publish waits for the held lock instead of returning within {held.TotalMilliseconds} ms");
            queueLock.Release();
            released = true;
            (await publish).ShouldBe(LegacyCheckNotePublicationService.PublishResult.Published);
        }
        finally
        {
            if (!released) queueLock.Release();
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await verify.AgentTaskLandNotifications.SingleAsync();
        note.Kind.ShouldBe(LandNotificationKind.LegacyCheckNote);
        note.QueueMessageId.ShouldNotBeNull("the inline handoff enqueued the note before the publish returned");
        note.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId)).AgentSessionId.ShouldBe(h.SessionId);
    }

    private static readonly int ScanQueueLockWaitMs = (int)AgentTaskLandNotificationService.ScanQueueLockWait.TotalMilliseconds;

    /// <summary>Ids that sort in the given order in the database (uuid order is the text order).</summary>
    private static List<Guid> OrderedIds(int count)
    {
        var prefix = Guid.NewGuid().ToString("N")[..20];
        return Enumerable.Range(1, count).Select(i => Guid.ParseExact($"{prefix}{i:x12}", "N")).ToList();
    }

    private static async Task<Guid> CloneSessionAsync(AppDbContext db, Guid source)
    {
        var clone = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == source);
        clone.Id = Guid.NewGuid();
        clone.DelegationTokenHash = null;
        db.AgentSessions.Add(clone);
        await db.SaveChangesAsync();
        return clone.Id;
    }

    private static async Task<LegacyCheckNotePublication> SeedDueCapturedAsync(AppDbContext db, BridgeQueueHarness h, Guid parent)
    {
        var episode = new CheckCompactionRecovery
        {
            Id = Guid.NewGuid(), PhysicalAgentId = h.AgentId, SessionId = h.SessionId,
            AcceptedStartedAt = DateTime.UtcNow, BoundaryIdentity = "b", BoundaryCreatedAt = DateTime.UtcNow,
            ContinuationCreatedAt = DateTime.UtcNow, ConfiguredThresholdMinutes = 10, DetectedAt = DateTime.UtcNow,
            State = CheckCompactionRecoveryState.AwaitingCheck,
        };
        var checkedId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        db.CheckCompactionRecoveries.Add(episode);
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkedId, RootTaskId = checkedId, Title = "subject", Goal = "watch", Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working, AgentId = h.AgentId,
            AgentSessionId = h.SessionId, WorkingDirectory = h.TempRoot, CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow, Attempt = 1, CheckCount = 1,
        });
        // A settled interpretation run: the scan produces the note and reconciles it inline.
        db.AgentTasks.Add(new AgentTask
        {
            Id = runId, RootTaskId = runId, Title = "run", Goal = "read", Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded, Result = "useful reading",
            AgentId = h.AgentId, AgentSessionId = h.SessionId, WorkingDirectory = h.TempRoot, CreatedAt = DateTime.UtcNow,
        });
        var publication = new LegacyCheckNotePublication
        {
            Id = Guid.NewGuid(), CheckedTaskId = checkedId, CheckedTaskAttempt = 1,
            CheckedTaskDispatchedAt = DateTime.UtcNow, CheckNumber = 1, RecoveryId = episode.Id,
            PhysicalAgentId = h.AgentId, InterpreterSessionId = h.SessionId,
            InterpreterAcceptedStartedAt = DateTime.UtcNow, ParentSessionId = parent,
            CapturedAt = DateTime.UtcNow, FactsSnapshotJson = "{}", RenderContextJson = "{}",
            InterpretationTaskId = runId, InterpretationDeadlineAt = DateTime.UtcNow.AddMinutes(5),
            State = LegacyCheckNoteState.Captured, SourceEventId = Guid.NewGuid(), NotificationId = Guid.NewGuid(),
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
        };
        db.LegacyCheckNotePublications.Add(publication);
        await db.SaveChangesAsync();
        return publication;
    }

    private sealed class ScanSignal : LandDeliveryBoundary
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public Action<Guid>? BeforeEnqueue { get; set; }
        /// <summary>Each before-enqueue identity with the number of scans completed when it was reached.</summary>
        public System.Collections.Concurrent.ConcurrentQueue<(int Scan, Guid Id)> EnqueueHits { get; } = new();
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "notification-scan") Interlocked.Increment(ref _count);
            if (boundary == "before-enqueue")
            {
                EnqueueHits.Enqueue((Count, identity));
                BeforeEnqueue?.Invoke(identity);
            }
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

using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DispatchBaseNotificationTests
{
    /// <summary>
    /// V-17 / G-40: a successful projection writes the Warning and the matching DispatchBase note
    /// together. PC-40 removes the note insert and this count goes 1 → 0.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_WarningCommitCreatesObligation(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var dispatch = await SeedDispatchEventAsync(db, task.Id, DateTime.UtcNow);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "obligation detail")], ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await using var projector = CreateContext(schema);
        await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(ids[0], ct);

        await using var verify = CreateContext(schema);
        var intent = await verify.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);
        (await verify.AgentTaskEvents.CountAsync(e => e.Id == intent.Id && e.Type == AgentTaskEventType.Warning, ct))
            .ShouldBe(1);
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.Id == intent.NotificationId, ct))
            .ShouldBe(1, "PC-40: omitting note insertion leaves this count at 0");
        intent.MaterializedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// V-17 / G-51: a projection SaveChanges that then fails to commit rolls the Warning, note and
    /// marker back together. PC-51 commits before SaveChanges so the after-save fault would leave a pair.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    [Arguments("before-save")]
    [Arguments("after-save")]
    public async Task C508_WarningCommitAtomic(string cut, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var dispatch = await SeedDispatchEventAsync(db, task.Id, DateTime.UtcNow);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "atomic detail")], ct);
        await db.SaveChangesAsync(ct);
        var original = await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct);

        IInterceptor interceptor = cut == "before-save"
            ? new ProjectionSaveFailure(original.NotificationId, afterSave: false)
            : new ProjectionSaveFailure(original.NotificationId, afterSave: true);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(schema.ConnectionString).AddInterceptors(interceptor).Options;
        await using (var projector = new AppDbContext(options))
            await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(original.Id, ct);
        ((ProjectionSaveFailure)interceptor).Fired.ShouldBeTrue();

        await using var check = CreateContext(schema);
        (await check.AgentTaskEvents.CountAsync(e => e.Id == original.Id, ct)).ShouldBe(0);
        (await check.AgentTaskLandNotifications.CountAsync(n => n.Id == original.NotificationId, ct)).ShouldBe(0);
        (await check.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == original.Id, ct))
            .MaterializedAt.ShouldBeNull("PC-51: an after-save fault must leave the marker null");

        await using var retry = CreateContext(schema);
        await new DispatchBaseWarningIntentService(retry, TimeProvider.System).MaterializeAsync(original.Id, ct);
        await using var final = CreateContext(schema);
        (await final.AgentTaskEvents.CountAsync(e => e.Id == original.Id, ct)).ShouldBe(1);
        (await final.AgentTaskLandNotifications.CountAsync(n => n.Id == original.NotificationId, ct)).ShouldBe(1);
        (await final.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == original.Id, ct))
            .MaterializedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// V-18 / G-59: a DispatchBase warning queued under the task conversation key with a report
    /// digest is not a completion note. PC-59 drops the SourceLandNotificationId exclusion.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_WarningsAreNotReports(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, h.SessionId);
        var dispatch = await SeedDispatchEventAsync(db, task.Id, DateTime.UtcNow);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "ordinary report")], ct);
        await db.SaveChangesAsync(ct);
        await new DispatchBaseWarningIntentService(db, TimeProvider.System).MaterializeAsync(ids[0], ct);
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == ids[0], ct);
        await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System)
            .ReconcileAsync(note.Id, ct);

        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId, ct);
        var reportDigest = DelegationNoteDigest.Compute("ordinary report");
        row.ConversationKey = $"task:{task.RootTaskId:N}";
        row.ContentDigest = reportDigest;
        task.Result = "ordinary report";
        await db.SaveChangesAsync(ct);

        (await AgentTaskCheckService.HasCompletionNoteAsync(db, h.SessionId, task.RootTaskId, ct))
            .ShouldBeFalse("PC-59: a keyed DispatchBase warning must not count as the completion note");
    }

    /// <summary>
    /// V-18 / G-63: same notification key + session with a changed digest is a conflict, not a
    /// reuse. PC-63 removes the ContentDigest comparison.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_WarningQueueDigestCollision(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, h.SessionId);
        var dispatch = await SeedDispatchEventAsync(db, task.Id, DateTime.UtcNow);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "digest collision")], ct);
        await db.SaveChangesAsync(ct);
        await new DispatchBaseWarningIntentService(db, TimeProvider.System).MaterializeAsync(ids[0], ct);
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == ids[0], ct);
        await new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System)
            .ReconcileAsync(note.Id, ct);

        await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(
            h.SessionId, note.Body, MessageSendMode.WhenIdle, ct,
            QueuedMessageOrigin.Delegation, sourceTaskId: note.TaskId,
            contentDigest: DelegationNoteDigest.Compute("different recovered payload"),
            deliverIfIdle: false, sourceLandNotificationId: note.Id));
    }

    /// <summary>
    /// V-20: upgrade through AddDispatchBaseWarningIntent preserves an old land pair, creates no
    /// intent backfill, and round-trips a requestless DispatchBase row. Indexes stay unique.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    public async Task C508_RequestlessNotificationUpgrade(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var cut = Array.FindIndex(migrations, m => m.EndsWith("_AddDispatchBaseWarningIntent", StringComparison.Ordinal));
        cut.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[cut - 1]);

        var taskId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var old = DateTime.UtcNow.AddDays(-4);
        const string body = "historical land pair, no receipt";
        var digest = DelegationNoteDigest.Compute(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel", "Attempt", "MaxAttempts",
                "WorkingDirectory", "Ephemeral", "Status", "ReplyTo", "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd",
                "Workspace")
            VALUES ({taskId}, {taskId}, 0, 'upgrade', 'upgrade fixture', 0, 0, 0, 0, 1, 'fixture', false, {(int)AgentTaskStatus.Succeeded},
                0, {Guid.NewGuid()}, {old}, 0, 0, 0, {(int)WorkspaceMode.Worktree})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskEvents" ("Id", "AgentTaskId", "Type", "Detail", "At")
            VALUES ({eventId}, {taskId}, {(int)AgentTaskEventType.Landed}, {body}, {old})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandRequests" ("Id", "TaskId", "RequestedAt", "ReplyTo", "State", "IsPending",
                "Attempt", "LastEvaluatedAt", "LastProgressAt", "HighestProgress", "HoldEpisode", "ConcurrencyToken")
            VALUES ({requestId}, {taskId}, {old}, 0, 0, FALSE, 0, {old}, {old}, -2, 0, {Guid.NewGuid()})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandNotifications" ("Id", "IsLegacy", "RequestId", "TaskId", "SourceEventId",
                "Kind", "ReplyTo", "Body", "ContentDigest", "CreatedAt", "NextAttemptAt", "EnqueueAttempts", "State",
                "ConcurrencyToken")
            VALUES ({noteId}, FALSE, {requestId}, {taskId}, {eventId}, {(int)LandNotificationKind.Outcome}, 0,
                {body}, {digest}, {old}, {old}, 0, {(int)LandNotificationState.NotRequired}, {Guid.NewGuid()})
            """);

        await db.GetService<IMigrator>().MigrateAsync();
        await db.GetService<IMigrator>().MigrateAsync();

        await using var observer = new AppDbContext(options);
        (await observer.AgentTaskDispatchWarningIntents.CountAsync(ct)).ShouldBe(0, "no historical intent backfill");
        var preserved = await observer.AgentTaskLandNotifications.SingleAsync(n => n.Id == noteId, ct);
        preserved.RequestId.ShouldBe(requestId);
        preserved.Body.ShouldBe(body);
        preserved.ContentDigest.ShouldBe(digest);
        preserved.Kind.ShouldBe(LandNotificationKind.Outcome);

        var parent = Guid.NewGuid();
        await SeedParentSessionAsync(observer, parent);
        var live = await SeedBareTaskAsync(observer, parent);
        var dispatch = await SeedDispatchEventAsync(observer, live.Id, DateTime.UtcNow);
        var ids = await new DispatchBaseWarningIntentService(observer, TimeProvider.System).CaptureAsync(
            live, dispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "requestless roundtrip")], ct);
        await observer.SaveChangesAsync(ct);
        await new DispatchBaseWarningIntentService(observer, TimeProvider.System).MaterializeAsync(ids[0], ct);
        var requestless = await observer.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == ids[0], ct);
        requestless.RequestId.ShouldBeNull();
        requestless.Kind.ShouldBe(LandNotificationKind.DispatchBase);

        await using var connection = new NpgsqlConnection(schema.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN (
                'IX_AgentTaskDispatchWarningIntents_DispatchEventId_WarningKey',
                'IX_AgentTaskDispatchWarningIntents_NotificationId')
              AND indexdef LIKE '%UNIQUE%'
            """, connection);
        Convert.ToInt32(await command.ExecuteScalarAsync(ct)).ShouldBe(2);
    }

    /// <summary>
    /// V-23 / G-67: an after-save fault inside the claim transaction leaves no final dispatch
    /// event, session or intent. PC-67 commits before SaveChanges so the fault would persist them.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    public async Task C508_ClaimIntentAtomic(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-claim-atomic");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        await SeedKeptSiblingAsync(db, repo, card.Id, "divergent sibling work");
        var parent = Guid.NewGuid();
        await SeedParentSessionAsync(db, parent);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parent);
        await db.SaveChangesAsync(ct);

        var interceptor = new ClaimCommitFailure(task.Id);
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, "master",
            interceptor: interceptor);
        await using var scope = provider.CreateAsyncScope();
        try { await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct); }
        catch (DbUpdateException) { /* Inspect persisted custody after the failed claim. */ }
        catch (IOException) { }
        interceptor.Fired.ShouldBeTrue();

        await using var check = CreateContext(schema);
        (await check.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id, ct)).ShouldBe(0);
        (await check.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Dispatched
                && e.Detail.StartsWith("Dispatched to agent"), ct)).ShouldBe(0);
        (await check.AgentSessions.CountAsync(ct)).ShouldBe(1, "only the original caller remains");
        var row = await check.AgentTasks.SingleAsync(t => t.Id == task.Id, ct);
        row.AgentSessionId.ShouldBeNull();
    }

    /// <summary>
    /// V-24 / G-71: Capture reads the locked claim's parent, not the outer tick snapshot.
    /// PC-71 passes the pre-claim route and would keep destination A.
    /// </summary>
    [Test]
    [Timeout(90_000)]
    public async Task C508_IntentCaptureRoute(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-capture-route");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        await SeedKeptSiblingAsync(db, repo, card.Id, "divergent sibling work");
        var destinationA = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        await SeedParentSessionAsync(db, destinationA);
        await SeedParentSessionAsync(db, destinationB);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, destinationA);
        await db.SaveChangesAsync(ct);

        var connection = schema.ConnectionString;
        await using var provider = CreateProvider(connection, repo.WorktreeRoot, "master",
            onLeaseAcquired: async () =>
            {
                await using var edit = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
                await edit.AgentTasks.Where(t => t.Id == task.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ParentSessionId, destinationB), ct);
            });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        await using var verify = CreateContext(schema);
        var intents = await verify.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        intents.ShouldNotBeEmpty();
        intents.ShouldAllBe(i => i.ParentSessionId == destinationB);
        foreach (var intent in intents)
        {
            intent.ContentDigest.ShouldBe(DispatchBaseNotificationPayload.Digest(
                AgentTaskReplyTo.Session, destinationB, intent.Body));
            intent.ContentDigest.ShouldNotBe(DispatchBaseNotificationPayload.Digest(
                AgentTaskReplyTo.Session, destinationA, intent.Body));
        }
    }

    /// <summary>
    /// V-27 / G-84: a bookkeeping SaveChanges failure must not discharge the pending intent.
    /// PC-84 would stamp MaterializedAt when recording the retry error fails.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_IntentRetryBookkeepingFailure(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = await SeedBareTaskAsync(db, Guid.NewGuid());
        var at = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);
        var dispatch = await SeedDispatchEventAsync(db, task.Id, at);
        var ids = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            task, dispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.MismatchKey, "bookkeeping")], ct);
        await db.SaveChangesAsync(ct);
        var originalDigest = (await db.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == ids[0], ct))
            .ContentDigest;
        await db.AgentTaskDispatchWarningIntents.Where(i => i.Id == ids[0])
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.ContentDigest, "0000"), ct);

        var fault = new BookkeepingSaveFailure(ids[0]);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(schema.ConnectionString).AddInterceptors(fault).Options;
        for (var pass = 0; pass < 2; pass++)
        {
            await using var projector = new AppDbContext(options);
            await new DispatchBaseWarningIntentService(projector, TimeProvider.System).MaterializeAsync(ids[0], ct);
        }
        fault.Fired.ShouldBeTrue();

        await using var pending = CreateContext(schema);
        var afterFaults = await pending.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == ids[0], ct);
        afterFaults.MaterializedAt.ShouldBeNull("PC-84: a bookkeeping fault must not stamp the marker");
        (await pending.AgentTaskEvents.CountAsync(e => e.Id == ids[0], ct)).ShouldBe(0);
        (await pending.AgentTaskLandNotifications.CountAsync(n => n.Id == afterFaults.NotificationId, ct)).ShouldBe(0);

        await pending.AgentTaskDispatchWarningIntents.Where(i => i.Id == ids[0])
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.ContentDigest, originalDigest), ct);

        await using var clean = CreateContext(schema);
        await new DispatchBaseWarningIntentService(clean, TimeProvider.System).MaterializeAsync(ids[0], ct);
        await using var final = CreateContext(schema);
        var settled = await final.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == ids[0], ct);
        settled.MaterializedAt.ShouldNotBeNull();
        (await final.AgentTaskEvents.CountAsync(e => e.Id == ids[0], ct)).ShouldBe(1);
        (await final.AgentTaskLandNotifications.CountAsync(n => n.Id == settled.NotificationId, ct)).ShouldBe(1);
    }

    /// <summary>
    /// V-27 / G-85..G-88: the hosted intent scan pages by Id, ignores task status/Attempt, and
    /// resets its cursor every cycle. 263 rows = three 128-row pages plus the specials.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task C508_IntentScanPaging(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        var statuses = new[]
        {
            AgentTaskStatus.Dispatched, AgentTaskStatus.Succeeded, AgentTaskStatus.Failed,
            AgentTaskStatus.Canceled, AgentTaskStatus.Queued,
        };
        var tasks = new List<AgentTask>();
        foreach (var status in statuses)
        {
            var row = await SeedBareTaskAsync(db, null, AgentTaskReplyTo.None);
            row.Status = status;
            row.Attempt = status == AgentTaskStatus.Queued ? 3 : 0;
            tasks.Add(row);
        }
        await db.SaveChangesAsync(ct);
        var dispatches = new Dictionary<Guid, AgentTaskEvent>();
        foreach (var owner in tasks)
            dispatches[owner.Id] = await SeedDispatchEventAsync(db, owner.Id, now);

        var futureId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var materializedId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var dueIds = new List<Guid>();
        for (var i = 0; i < 263; i++)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}");
            var notificationId = Guid.Parse($"00000000-0000-0000-0001-{i + 1:D12}");
            var owner = tasks[i % tasks.Count];
            var intent = NewIntent(owner, dispatches[owner.Id], $"k{i}", $"paging {i}", now, id, notificationId);
            intent.NextAttemptAt = id == futureId ? now.AddHours(1) : now.AddMinutes(-1);
            if (id == materializedId) intent.MaterializedAt = now;
            if (owner.Attempt == 3) intent.Attempt = 0;
            db.AgentTaskDispatchWarningIntents.Add(intent);
            if (id != futureId && id != materializedId) dueIds.Add(id);
        }
        await db.SaveChangesAsync(ct);
        dueIds.Count.ShouldBe(261);

        var scans = new ScanBoundary();
        await using var provider = CreateProvider(schema.ConnectionString, Path.GetTempPath(), "master", boundary: scans);
        await RunIntentScanAsync(provider, scans, cycles: 1, ct);

        await using var afterFirst = CreateContext(schema);
        foreach (var id in dueIds)
        {
            var row = await afterFirst.AgentTaskDispatchWarningIntents.AsNoTracking().SingleAsync(i => i.Id == id, ct);
            row.MaterializedAt.ShouldNotBeNull($"due intent {id:N} must materialize across page three");
            (await afterFirst.AgentTaskLandNotifications.CountAsync(n => n.Id == row.NotificationId, ct)).ShouldBe(1);
        }
        (await afterFirst.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == futureId, ct))
            .MaterializedAt.ShouldBeNull();
        (await afterFirst.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == materializedId, ct))
            .MaterializedAt.ShouldNotBeNull();

        await afterFirst.AgentTaskDispatchWarningIntents.Where(i => i.Id == futureId)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.NextAttemptAt, now.AddMinutes(-1)), ct);

        await RunIntentScanAsync(provider, scans, cycles: 1, ct);
        await using var afterSecond = CreateContext(schema);
        (await afterSecond.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == futureId, ct))
            .MaterializedAt.ShouldNotBeNull("PC-88: a newly due low Id must be revisited after cursor reset");
    }

    /// <summary>
    /// V-27 / G-89, G-90: one poison row cannot abort later rows, and an intent-query fault cannot
    /// suppress the notification pass.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("row")]
    [Arguments("query")]
    public async Task C508_IntentScanIsolation(string fault, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        var poisonTask = await SeedBareTaskAsync(db, null, AgentTaskReplyTo.None);
        var healthyTask = await SeedBareTaskAsync(db, null, AgentTaskReplyTo.None);
        var poisonDispatch = await SeedDispatchEventAsync(db, poisonTask.Id, now);
        var healthyDispatch = await SeedDispatchEventAsync(db, healthyTask.Id, now);
        var poisonId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var healthyId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var poison = NewIntent(poisonTask, poisonDispatch, DispatchBaseNotificationPayload.MismatchKey, "poison", now, poisonId);
        poison.NextAttemptAt = now.AddMinutes(-1);
        var healthy = NewIntent(healthyTask, healthyDispatch, DispatchBaseNotificationPayload.MismatchKey, "healthy", now, healthyId);
        healthy.NextAttemptAt = now.AddMinutes(-1);
        db.AgentTaskDispatchWarningIntents.AddRange(poison, healthy);

        var dueNoteTask = await SeedBareTaskAsync(db, h.SessionId);
        var dueDispatch = await SeedDispatchEventAsync(db, dueNoteTask.Id, now);
        var dueIds = await new DispatchBaseWarningIntentService(db, TimeProvider.System).CaptureAsync(
            dueNoteTask, dueDispatch,
            [new DispatchWarningDraft(DispatchBaseNotificationPayload.DefaultUnresolvedKey, "unrelated due note")], ct);
        await db.SaveChangesAsync(ct);
        await new DispatchBaseWarningIntentService(db, TimeProvider.System).MaterializeAsync(dueIds[0], ct);
        var dueNote = await db.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == dueIds[0], ct);
        dueNote.State = LandNotificationState.Queued;
        dueNote.QueueMessageId = null;
        dueNote.NextAttemptAt = now.AddMinutes(-1);
        await db.SaveChangesAsync(ct);

        IInterceptor? interceptor = fault == "query" ? new IntentScanQueryFault() : null;
        var scans = new ScanBoundary { PoisonId = fault == "row" ? poisonId : Guid.Empty };
        await using var provider = CreateProvider(schema.ConnectionString, Path.GetTempPath(), "master",
            interceptor: interceptor, boundary: scans);
        await RunIntentScanAsync(provider, scans, cycles: 1, ct);

        await using var verify = CreateContext(schema);
        if (fault == "row")
        {
            (await verify.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == poisonId, ct))
                .MaterializedAt.ShouldBeNull();
            var recovered = await verify.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == healthyId, ct);
            recovered.MaterializedAt.ShouldNotBeNull("PC-89: a later healthy row must still materialize");
            (await verify.AgentTaskLandNotifications.CountAsync(n => n.Id == recovered.NotificationId, ct)).ShouldBe(1);
        }
        else
        {
            (await verify.AgentTaskDispatchWarningIntents.CountAsync(i => i.MaterializedAt != null && i.Id != dueIds[0], ct))
                .ShouldBe(0, "the armed intent-query fault must prevent this pass's intent materialization");
            var note = await verify.AgentTaskLandNotifications.SingleAsync(n => n.Id == dueNote.Id, ct);
            note.QueueMessageId.ShouldNotBeNull("PC-90: notification pass must still enqueue the unrelated due note");
        }
    }

    /// <summary>
    /// V-23 / G-106: a refused or aborted claim never commits warning intent. The worktree-created
    /// Dispatched event on a failed progress baseline is not a capture authorization.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    [Arguments("lease")]
    [Arguments("sibling-hold")]
    [Arguments("stale-claim")]
    [Arguments("invalid-ref")]
    [Arguments("failed-baseline")]
    [Arguments("optional-expiry")]
    public async Task C508_RefusedClaimHasNoIntent(string reason, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-refused-" + reason);
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, "divergent sibling work");
        var parent = Guid.NewGuid();
        await SeedParentSessionAsync(db, parent);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parent);
        if (reason == "sibling-hold") sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        if (reason == "invalid-ref") task.WorktreeBaseRequestedRef = "no-such-c508-ref";
        if (reason == "optional-expiry")
        {
            task.Role = AgentTaskRole.Distill;
            task.ExecutionDeadlineAt = DateTime.UtcNow.AddMinutes(-1);
        }
        await db.SaveChangesAsync(ct);

        RepositoryLease? occupied = null;
        try
        {
            if (reason == "lease")
            {
                occupied = await new RepositoryMutationLease(new LandingGit()).TryAcquireAsync(repo.Path, ct);
                occupied.ShouldNotBeNull();
            }

            Func<Task>? onLease = reason == "stale-claim"
                ? async () =>
                {
                    await using var edit = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                    await edit.AgentTasks.Where(t => t.Id == task.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()), ct);
                }
                : null;
            ITaskProgressGit? progress = reason == "failed-baseline" ? new FakeTaskProgressGit() : null;
            await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, "master",
                onLeaseAcquired: onLease, progressGit: progress);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        }
        finally
        {
            if (occupied is not null) await occupied.DisposeAsync();
        }

        await using var verify = CreateContext(schema);
        (await verify.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id, ct)).ShouldBe(0);
        var row = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id, ct);
        switch (reason)
        {
            case "lease":
            case "sibling-hold":
            case "stale-claim":
                row.Status.ShouldBe(AgentTaskStatus.Queued);
                row.AgentSessionId.ShouldBeNull();
                break;
            case "optional-expiry":
                row.Status.ShouldBe(AgentTaskStatus.Canceled);
                row.AgentSessionId.ShouldBeNull();
                break;
            case "invalid-ref":
            case "failed-baseline":
                row.Status.ShouldBe(AgentTaskStatus.Failed);
                row.AgentSessionId.ShouldBeNull();
                break;
        }

        if (reason == "failed-baseline")
        {
            var created = await verify.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Dispatched)
                .ToListAsync(ct);
            created.ShouldContain(e => e.Detail.Contains("Worktree created", StringComparison.Ordinal));
            created.ShouldNotContain(e => e.Detail.StartsWith("Dispatched to agent", StringComparison.Ordinal));
        }
    }

    private static async Task RunIntentScanAsync(
        ServiceProvider provider, ScanBoundary scans, int cycles, CancellationToken ct)
    {
        var worker = new AgentTaskLandNotificationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(180));
        await worker.StartAsync(stop.Token);
        try
        {
            for (var i = 0; i < cycles; i++)
                await scans.IntentPulse.WaitAsync(TimeSpan.FromSeconds(90), ct);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private sealed class ScanBoundary : LandDeliveryBoundary
    {
        public Guid PoisonId { get; set; }
        public SemaphoreSlim IntentPulse { get; } = new(0);
        public SemaphoreSlim NotificationPulse { get; } = new(0);

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary == "dispatch-warning-before-materialize" && identity == PoisonId && PoisonId != Guid.Empty)
                throw new InvalidOperationException("owned poison intent");
            if (boundary == "dispatch-warning-intent-scan") IntentPulse.Release();
            if (boundary == "notification-scan") NotificationPulse.Release();
            return Task.CompletedTask;
        }
    }

    private sealed class ProjectionSaveFailure(Guid noteId, bool afterSave) : IInterceptor, ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (!afterSave && ShouldFire(eventData))
            {
                Fired = true;
                throw new IOException("owned projection failure before SaveChanges");
            }
            return result;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            SavingChanges(eventData, result);
            return ValueTask.FromResult(result);
        }

        public int SavedChanges(SaveChangesCompletedEventData eventData, int result) => result;
        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
            => ValueTask.FromResult(result);
        public void SaveChangesFailed(DbContextErrorEventData eventData) { }
        public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default) => Task.CompletedTask;

        public InterceptionResult TransactionCommitting(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            if (afterSave && ShouldFire(eventData))
            {
                Fired = true;
                throw new IOException("owned projection failure after SaveChanges");
            }
            return result;
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            TransactionCommitting(transaction, eventData, result);
            return ValueTask.FromResult(result);
        }

        private bool ShouldFire(DbContextEventData eventData) =>
            !Fired && eventData.Context is AppDbContext db
            && db.ChangeTracker.Entries<AgentTaskLandNotification>().Any(e => e.Entity.Id == noteId);
    }

    private sealed class ClaimCommitFailure(Guid taskId) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            if (!Fired && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTaskDispatchWarningIntent>().Any(e => e.Entity.TaskId == taskId))
            {
                Fired = true;
                throw new IOException("owned claim failure after SaveChanges");
            }
            return result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            TransactionCommitting(transaction, eventData, result);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BookkeepingSaveFailure(Guid intentId) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTaskDispatchWarningIntent>().Any(e =>
                    e.Entity.Id == intentId && e.Entity.LastErrorCode != null && e.Entity.MaterializedAt is null))
            {
                Fired = true;
                throw new IOException("owned bookkeeping save failure");
            }
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            SavingChanges(eventData, result);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class IntentScanQueryFault : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.Contains("AgentTaskDispatchWarningIntents", StringComparison.Ordinal)
                && command.CommandText.Contains("MaterializedAt", StringComparison.Ordinal)
                && command.CommandText.Contains("NextAttemptAt", StringComparison.Ordinal)
                && !command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("owned intent-scan query fault");
            }
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken ct = default)
        {
            ReaderExecuting(command, eventData, result);
            return ValueTask.FromResult(result);
        }
    }
}

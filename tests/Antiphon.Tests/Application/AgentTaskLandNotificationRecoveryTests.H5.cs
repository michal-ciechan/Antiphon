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
    [Test]
    public async Task Legacy_captured_scan_isolates_a_poison_row()
    {
        var (schema, harness, ids) = await SeedCapturedAsync(2);
        await using (schema)
        await using (harness)
        {
            var poison = ids[0];
            var later = ids[1];
            await RunScanAsync(harness, new PoisonBoundary(poison));
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            (await verify.LegacyCheckNotePublications.SingleAsync(p => p.Id == later.Id)).State
                .ShouldBe(LegacyCheckNoteState.Produced);
        }
    }

    [Test]
    public async Task Legacy_captured_retry_obeys_its_due_time()
    {
        var (schema, harness, ids) = await SeedCapturedAsync(1, due: DateTime.UtcNow.AddHours(1));
        await using (schema)
        await using (harness)
        {
            var boundary = new CountingBoundary();
            await RunScanAsync(harness, boundary);
            boundary.Ids.ShouldNotContain(ids[0].Id);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await db.LegacyCheckNotePublications.Where(p => p.Id == ids[0].Id)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
            await RunScanAsync(harness, boundary);
            boundary.Ids.ShouldContain(ids[0].Id);
        }
    }

    [Test]
    public async Task Legacy_captured_pass_excludes_produced_rows()
    {
        var (schema, harness, ids) = await SeedCapturedAsync(1);
        await using (schema)
        await using (harness)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var produced = new LegacyCheckNotePublication
            {
                Id = Guid.NewGuid(), CheckedTaskId = Guid.NewGuid(), CheckedTaskAttempt = 1,
                CheckedTaskDispatchedAt = DateTime.UtcNow, CheckNumber = 9,
                RecoveryId = ids[0].RecoveryId, PhysicalAgentId = harness.AgentId,
                InterpreterSessionId = harness.SessionId, InterpreterAcceptedStartedAt = DateTime.UtcNow,
                ParentSessionId = harness.SessionId, CapturedAt = DateTime.UtcNow, FactsSnapshotJson = "{}",
                RenderContextJson = "{}", InterpretationDeadlineAt = DateTime.UtcNow, State = LegacyCheckNoteState.Produced,
                SourceEventId = Guid.NewGuid(), NotificationId = Guid.NewGuid(), NextAttemptAt = DateTime.UtcNow,
                Body = "already produced body", ContentDigest = "digest", ProducedAt = DateTime.UtcNow,
            };
            db.LegacyCheckNotePublications.Add(produced);
            await db.SaveChangesAsync();
            var boundary = new CountingBoundary();
            await RunScanAsync(harness, boundary);
            boundary.Ids.ShouldNotContain(produced.Id);
            boundary.Ids.ShouldContain(ids[0].Id);
        }
    }

    [Test]
    public async Task Legacy_note_uses_check_origin() =>
        await AssertQueuedAsync((row, _) => row.Origin.ShouldBe(QueuedMessageOrigin.Check));

    [Test]
    public async Task Legacy_note_keeps_the_original_check_conversation() =>
        await AssertQueuedAsync((row, taskId) => row.ConversationKey.ShouldBe($"check:{taskId:N}"));

    [Test]
    public async Task Legacy_note_never_stamps_task_completion() =>
        await AssertQueuedAsync((row, _) => row.Origin.ShouldBe(QueuedMessageOrigin.Check));

    [Test]
    public async Task Legacy_note_keeps_the_checked_task_source() =>
        await AssertQueuedAsync((row, taskId) => row.SourceTaskId.ShouldBe(taskId));

    [Test]
    public async Task Legacy_parked_note_is_not_woken_by_adoption()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var wakes = new CountingFlush();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedNotificationAsync(db, harness.SessionId, harness.AgentId);
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = harness.SessionId, Body = note.Body, Status = QueuedMessageStatus.Pending,
            Origin = QueuedMessageOrigin.Check, SourceTaskId = note.TaskId, SourceLandNotificationId = note.Id,
            ContentDigest = note.ContentDigest, DeliveryAttempts = 3, CreatedAt = DateTime.UtcNow,
            ConversationKey = $"check:{note.TaskId:N}",
        };
        db.SessionQueuedMessages.Add(row);
        await db.SaveChangesAsync();
        await new AgentTaskLandNotificationService(db, harness.Queue, wakes, harness.Runtime, TimeProvider.System)
            .ReconcileAsync(note.Id, CancellationToken.None);
        wakes.Count.ShouldBe(0);
    }

    [Test]
    public async Task Legacy_unavailable_parent_is_not_redirected()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var original = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var replacement = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedNotificationAsync(db, original.SessionId, original.AgentId);
        await db.AgentSessions.Where(s => s.Id == original.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Stopped));
        await db.AgentTasks.Where(t => t.Id == note.TaskId)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.ParentSessionId, replacement.SessionId));
        await new AgentTaskLandNotificationService(db, original.Queue, new CompletionNoteFlushQueue(), original.Runtime, TimeProvider.System)
            .ReconcileAsync(note.Id, CancellationToken.None);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == replacement.SessionId)).ShouldBe(0);
        (await db.AgentTaskLandNotifications.SingleAsync()).ParentSessionId.ShouldBe(original.SessionId);
    }

    [Test]
    public async Task Legacy_missing_authoritative_queue_row_remains_unresolved()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedNotificationAsync(db, harness.SessionId, harness.AgentId);
        var rowId = Guid.NewGuid();
        note.QueueMessageId = rowId;
        note.State = LandNotificationState.AwaitingReceipt;
        await db.SaveChangesAsync();
        var service = new AgentTaskLandNotificationService(db, harness.Queue, new CompletionNoteFlushQueue(), harness.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == note.Id)).ShouldBe(0);
        (await db.AgentTaskLandNotifications.SingleAsync()).QueueMessageId.ShouldBe(rowId);
    }

    [Test]
    public async Task Legacy_captured_scan_reaches_later_pages()
    {
        var (schema, harness, ids) = await SeedCapturedAsync(263, settledRun: false);
        await using (schema)
        await using (harness)
        {
            var boundary = new CountingBoundary();
            await RunScanAsync(harness, boundary);
            var expected = ids.OrderBy(row => row.Id).Skip(256).Select(row => row.Id).ToArray();
            boundary.Ids.OrderBy(id => id).Skip(256).ShouldBe(expected);
        }
    }

    private static async Task AssertQueuedAsync(Action<SessionQueuedMessage, Guid> assert)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedNotificationAsync(db, harness.SessionId, harness.AgentId, childOfRoot: true);
        await new AgentTaskLandNotificationService(db, harness.Queue, new CompletionNoteFlushQueue(), harness.Runtime, TimeProvider.System)
            .ReconcileAsync(note.Id, CancellationToken.None);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == note.TaskId);
        task.CompletionNoteQueuedAt.ShouldBeNull();
        assert(row, note.TaskId);
    }

    private static async Task RunScanAsync(BridgeQueueHarness harness, CheckCompactionBoundary boundary)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(harness.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(boundary);
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<LegacyCheckNotePublicationService>();
        services.AddSingleton(harness.Queue);
        services.AddSingleton(harness.Runtime);
        await using var provider = services.BuildServiceProvider();
        var worker = new AgentTaskLandNotificationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        var seenPasses = boundary is CountingBoundary started ? started.Passes : 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await worker.StartAsync(stop.Token);
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (boundary is CountingBoundary counting && counting.Passes > seenPasses)
                break;
            if (boundary is PoisonBoundary poison && poison.Finished)
                break;
            await Task.Delay(boundary is PoisonBoundary ? 200 : 50);
        }

        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
    }

    private static async Task<(IsolatedTestSchema Schema, BridgeQueueHarness Harness, List<LegacyCheckNotePublication> Rows)> SeedCapturedAsync(
        int count, DateTime? due = null, bool settledRun = true)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var harness = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var episode = new CheckCompactionRecovery
        {
            Id = Guid.NewGuid(), PhysicalAgentId = harness.AgentId, SessionId = harness.SessionId,
            AcceptedStartedAt = DateTime.UtcNow, BoundaryIdentity = "b", BoundaryCreatedAt = DateTime.UtcNow,
            ContinuationCreatedAt = DateTime.UtcNow, ConfiguredThresholdMinutes = 10, DetectedAt = DateTime.UtcNow,
            State = CheckCompactionRecoveryState.AwaitingCheck,
        };
        db.CheckCompactionRecoveries.Add(episode);
        var rows = new List<LegacyCheckNotePublication>();
        for (var i = 0; i < count; i++)
        {
            var runId = Guid.NewGuid();
            var checkedId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = checkedId, RootTaskId = checkedId, Title = "subject", Goal = "watch", Role = AgentTaskRole.Code,
                Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working, AgentId = harness.AgentId,
                AgentSessionId = harness.SessionId, WorkingDirectory = harness.TempRoot, CreatedAt = DateTime.UtcNow,
                DispatchedAt = DateTime.UtcNow, Attempt = 1, CheckCount = i + 1,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = runId, RootTaskId = runId, Title = "run", Goal = "read", Role = AgentTaskRole.Check,
                Kind = AgentTaskKind.Worker,
                Status = settledRun ? AgentTaskStatus.Succeeded : AgentTaskStatus.Working,
                Result = settledRun ? "useful reading " + i : null,
                AgentId = harness.AgentId, AgentSessionId = harness.SessionId, WorkingDirectory = harness.TempRoot,
                CreatedAt = DateTime.UtcNow,
            });
            rows.Add(new LegacyCheckNotePublication
            {
                Id = Guid.NewGuid(), CheckedTaskId = checkedId, CheckedTaskAttempt = 1,
                CheckedTaskDispatchedAt = DateTime.UtcNow, CheckNumber = i + 1, RecoveryId = episode.Id,
                PhysicalAgentId = harness.AgentId, InterpreterSessionId = harness.SessionId,
                InterpreterAcceptedStartedAt = DateTime.UtcNow, ParentSessionId = harness.SessionId,
                CapturedAt = DateTime.UtcNow, FactsSnapshotJson = "{}", RenderContextJson = "{}",
                InterpretationTaskId = runId, InterpretationDeadlineAt = DateTime.UtcNow.AddMinutes(5),
                State = LegacyCheckNoteState.Captured, SourceEventId = Guid.NewGuid(), NotificationId = Guid.NewGuid(),
                NextAttemptAt = due ?? DateTime.UtcNow.AddMinutes(-1),
            });
        }

        db.LegacyCheckNotePublications.AddRange(rows);
        await db.SaveChangesAsync();
        return (schema, harness, rows.OrderBy(row => row.Id).ToList());
    }

    private static async Task<AgentTaskLandNotification> SeedNotificationAsync(
        AppDbContext db, Guid sessionId, Guid agentId, bool childOfRoot = false)
    {
        var root = Guid.NewGuid();
        var taskId = childOfRoot ? Guid.NewGuid() : root;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = root, Title = "subject", Goal = "watch", Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working, AgentId = agentId,
            AgentSessionId = sessionId, ParentSessionId = sessionId, ReplyTo = AgentTaskReplyTo.Session,
            WorkingDirectory = Path.GetTempPath(), CreatedAt = DateTime.UtcNow,
        });
        var eventId = Guid.NewGuid();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = eventId, AgentTaskId = taskId, Type = AgentTaskEventType.Check,
            Detail = "legacy check note", At = DateTime.UtcNow,
        });
        var note = new AgentTaskLandNotification
        {
            Id = Guid.NewGuid(), TaskId = taskId, SourceEventId = eventId,
            Kind = LandNotificationKind.LegacyCheckNote, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = sessionId, Body = "legacy check note body", ContentDigest = "digest",
            CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow, State = LandNotificationState.Queued,
        };
        db.AgentTaskLandNotifications.Add(note);
        await db.SaveChangesAsync();
        return note;
    }

    private sealed class CountingBoundary : CheckCompactionBoundary
    {
        public List<Guid> Ids { get; } = [];
        public int Passes { get; private set; }

        public override Task ReachedAsync(string boundary, Guid operationId, CancellationToken ct)
        {
            if (boundary != "legacy-captured-scan")
                return Task.CompletedTask;
            if (operationId == Guid.Empty)
                Passes++;
            else
                Ids.Add(operationId);
            return Task.CompletedTask;
        }
    }

    private sealed class PoisonBoundary(LegacyCheckNotePublication poison) : CheckCompactionBoundary
    {
        public bool Finished { get; private set; }

        public override Task ReachedAsync(string boundary, Guid operationId, CancellationToken ct)
        {
            if (boundary == "legacy-captured-scan" && operationId == Guid.Empty)
                Finished = true;
            if (boundary == "legacy-captured-scan" && operationId == poison.Id)
                throw new InvalidOperationException("poison row");
            return Task.CompletedTask;
        }
    }

    private sealed class CountingFlush : CompletionNoteFlushQueue
    {
        public int Count { get; private set; }

        public override bool TryEnqueue(Guid sessionId)
        {
            Count++;
            return base.TryEnqueue(sessionId);
        }
    }
}

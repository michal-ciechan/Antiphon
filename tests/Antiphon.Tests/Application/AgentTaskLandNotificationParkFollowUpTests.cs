using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1773. Witnesses for the CARD-1769 advisories: backoff through the harness clock,
/// a park entry dropped after a confirm outside the hosted scan, no late confirm of a
/// canceled note, one transcript pull per session per pass, the prompt-stamp index, and
/// a missing queue row parked only after three misses.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("card-1773-park-follow-up")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandNotificationParkFollowUpTests
{
    private const string PromptStampIndex = "IX_TranscriptEntries_AgentSessionId_Kind_Sequence";

    private static IsolatedTestSchema _schema = null!;
    private static BridgeQueueHarness _harness = null!;
    private static FakeTimeProvider _clock = null!;
    private static DbContextOptions<AppDbContext> _options = null!;
    private static StampObserver _stamps = null!;
    private static long _sequence = 1;

    [Before(Class), Timeout(180_000)]
    public static async Task Start()
    {
        _schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _stamps = new StampObserver();
        _harness = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = _schema.ConnectionString,
            TimeProvider = _clock,
            ConfigureDbContext = options => options.AddInterceptors(_stamps),
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
            },
        });
        _options = TestDbFixture.CreateDbContextOptions(_schema.ConnectionString, _stamps);
    }

    [After(Class)]
    public static async Task Stop()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
        if (_schema is not null)
            await _schema.DisposeAsync();
    }

    [Test, Timeout(180_000)]
    public async Task C1773_BackoffDoublesAndCapsAtFiveMinutes(CancellationToken cancellationToken)
    {
        var seeded = await SeedAsync(attempts: 0);
        var worker = Worker();
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        var token = await TokenAsync(seeded.NoteId);

        await AssertQuiet(worker, seeded.NoteId, token, TimeSpan.FromSeconds(59), cancellationToken);
        token = await AssertWoke(worker, seeded.NoteId, token, TimeSpan.FromSeconds(2), cancellationToken);

        await AssertQuiet(worker, seeded.NoteId, token, TimeSpan.FromSeconds(119), cancellationToken);
        token = await AssertWoke(worker, seeded.NoteId, token, TimeSpan.FromSeconds(2), cancellationToken);

        await AssertQuiet(worker, seeded.NoteId, token, TimeSpan.FromMinutes(4) - TimeSpan.FromSeconds(1), cancellationToken);
        token = await AssertWoke(worker, seeded.NoteId, token, TimeSpan.FromSeconds(2), cancellationToken);

        await AssertQuiet(worker, seeded.NoteId, token, TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1), cancellationToken);
        await AssertWoke(worker, seeded.NoteId, token, TimeSpan.FromSeconds(2), cancellationToken);
    }

    [Test, Timeout(180_000)]
    public async Task C1773_ExternalConfirmDropsTheParkEntry(CancellationToken cancellationToken)
    {
        var leak = await SeedAsync(attempts: 0);
        var control = await SeedAsync(attempts: 0);
        var worker = Worker();
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);

        await using (var db = new AppDbContext(_options))
        {
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.Id == leak.MessageId, cancellationToken);
            message.DeliveryAttempts = 1;
            message.LastDeliveryBaselineSequence = 0;
            db.TranscriptEntries.Add(Prompt(leak.SessionId, leak.Body));
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var scope = _harness.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                .ReconcileAsync(leak.NoteId, cancellationToken);
        }

        (await StateAsync(leak.NoteId)).ShouldBe(LandNotificationState.Confirmed);
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);

        _stamps.Reset();
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        _stamps.SawSession(leak.SessionId).ShouldBeFalse(
            "a note confirmed outside the hosted scan must leave the park map on the next pass");
        _stamps.SawSession(control.SessionId).ShouldBeTrue(
            "a note that is still awaiting receipt stays in the stamp read");
    }

    [Test, Timeout(180_000)]
    public async Task C1773_CanceledNoteDoesNotLateConfirm(CancellationToken cancellationToken)
    {
        var queued = await SeedAsync(attempts: 1);
        var already = await SeedAsync(attempts: 1, state: LandNotificationState.Canceled);
        await using (var db = new AppDbContext(_options))
        {
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.MessageId, cancellationToken);
            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = _clock.GetUtcNow().UtcDateTime;
            message.LastDeliveryBaselineSequence = 0;
            var sent = await db.SessionQueuedMessages.SingleAsync(m => m.Id == already.MessageId, cancellationToken);
            sent.Status = QueuedMessageStatus.Sent;
            sent.LastDeliveryBaselineSequence = 0;
            db.TranscriptEntries.Add(Prompt(queued.SessionId, queued.Body));
            db.TranscriptEntries.Add(Prompt(already.SessionId, already.Body));
            await db.SaveChangesAsync(cancellationToken);
        }

        await using var scope = _harness.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>();
        await service.ReconcileAsync(queued.NoteId, cancellationToken);
        await service.ReconcileAsync(already.NoteId, cancellationToken);

        (await StateAsync(queued.NoteId)).ShouldBe(LandNotificationState.Canceled);
        (await ConfirmedAtAsync(queued.NoteId)).ShouldBeNull();
        (await StateAsync(already.NoteId)).ShouldBe(LandNotificationState.Canceled);
        (await ConfirmedAtAsync(already.NoteId)).ShouldBeNull();
    }

    [Test, Timeout(180_000)]
    public async Task C1773_OneTranscriptPullPerSessionPerPass(CancellationToken cancellationToken)
    {
        var session = await CopySessionAsync();
        var first = await SeedAsync(attempts: 1, sessionId: session);
        var second = await SeedAsync(attempts: 1, sessionId: session);
        var other = await SeedAsync(attempts: 1);
        var pulls = new Dictionary<Guid, int>();
        _harness.Runner.TranscriptPull = (id, _) =>
        {
            pulls[id] = pulls.GetValueOrDefault(id) + 1;
            return Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        };
        try
        {
            await Worker().ScanLandNotificationsOnceAsync(cancellationToken);
            pulls.GetValueOrDefault(session).ShouldBe(1,
                "two notes on one session share one catch-up pull in the pass");
            pulls.GetValueOrDefault(other.SessionId).ShouldBe(1);
            first.NoteId.ShouldNotBe(second.NoteId);
        }
        finally
        {
            _harness.Runner.TranscriptPull = null;
        }
    }

    [Test, Timeout(180_000)]
    public async Task C1773_PromptStampIndexIncludesKind(CancellationToken cancellationToken)
    {
        var session = await CopySessionAsync();
        await using var db = new AppDbContext(_options);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "TranscriptEntries" ("Id", "AgentSessionId", "Sequence", "Kind", "Text", "CreatedAt")
            SELECT gen_random_uuid(), {0}, g,
                   CASE WHEN g IN (10, 20) THEN 'UserPrompt' ELSE 'AssistantText' END,
                   'x', NOW()
            FROM generate_series(1, 4000) g
            """, session);
        var maxPrompt = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .MaxAsync(t => (long?)t.Sequence, cancellationToken);
        maxPrompt.ShouldBe(20);

        await db.Database.ExecuteSqlRawAsync("""ANALYZE "TranscriptEntries";""");
        var plan = new List<string>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT)
                SELECT t."AgentSessionId", MAX(t."Sequence")
                FROM "TranscriptEntries" t
                WHERE t."AgentSessionId" = @session
                  AND t."Kind" IN ('UserPrompt', 'QueuedUserPrompt')
                GROUP BY t."AgentSessionId"
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "session";
            parameter.Value = session;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                plan.Add(reader.GetString(0));
        }

        Console.WriteLine("CARD-1773 stamp plan");
        foreach (var line in plan)
            Console.WriteLine(line);

        var index = db.Model.FindEntityType(typeof(TranscriptEntry))!
            .GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() == PromptStampIndex);
        index.ShouldNotBeNull();
        index!.Properties.Select(property => property.Name).ShouldBe(["AgentSessionId", "Kind", "Sequence"]);
index.IsCreatedConcurrently().ShouldBe(true);
        var present = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_indexes WHERE indexname = {PromptStampIndex}
            """).SingleAsync(cancellationToken);
        present.ShouldBe(1);
        string.Join('\n', plan).ShouldContain(PromptStampIndex);
    }

    [Test, Timeout(180_000)]
    public async Task C1773_MissingQueueParksAfterThreeMisses(CancellationToken cancellationToken)
    {
        var seeded = await SeedAsync(attempts: 0);
        await using (var db = new AppDbContext(_options))
        {
            await db.SessionQueuedMessages.Where(m => m.Id == seeded.MessageId).ExecuteDeleteAsync(cancellationToken);
        }

        var worker = Worker();
        var token = await TokenAsync(seeded.NoteId);
        for (var miss = 1; miss <= 3; miss++)
        {
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            var next = await TokenAsync(seeded.NoteId);
            next.ShouldNotBe(token, $"missing-queue miss {miss} did not reconcile");
            (await ErrorAsync(seeded.NoteId)).ShouldBe("keyed_queue_row_missing");
            token = next;
        }

        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        (await TokenAsync(seeded.NoteId)).ShouldBe(token, "the fourth pass reconciled a missing queue row that should be parked");

        await using (var db = new AppDbContext(_options))
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = seeded.MessageId,
                AgentSessionId = seeded.SessionId,
                Body = seeded.Body,
                Status = QueuedMessageStatus.Pending,
                Sequence = _sequence++,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                SourceLandNotificationId = seeded.NoteId,
                ContentDigest = seeded.NoteId.ToString("N"),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        (await TokenAsync(seeded.NoteId)).ShouldNotBe(token, "the queue row returning did not wake the parked note");
    }

    private static async Task AssertQuiet(AgentTaskLandNotificationHostedService worker, Guid noteId, Guid token,
        TimeSpan by, CancellationToken cancellationToken)
    {
        _clock.Advance(by);
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        (await TokenAsync(noteId)).ShouldBe(token, $"park window of {by} reconciled the note");
    }

    private static async Task<Guid> AssertWoke(AgentTaskLandNotificationHostedService worker, Guid noteId, Guid token,
        TimeSpan by, CancellationToken cancellationToken)
    {
        _clock.Advance(by);
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        var next = await TokenAsync(noteId);
        next.ShouldNotBe(token, $"advancing {by} left the parked note in place");
        return next;
    }

    private static AgentTaskLandNotificationHostedService Worker() => new(
        _harness.Provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<AgentTaskLandNotificationHostedService>.Instance);

    private static async Task<Seeded> SeedAsync(int attempts, Guid? sessionId = null,
        LandNotificationState state = LandNotificationState.AwaitingReceipt)
    {
        var session = sessionId ?? await CopySessionAsync();
        await using var db = new AppDbContext(_options);
        var now = _clock.GetUtcNow().UtcDateTime;
        var taskId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var body = "c1773-park-follow-up-" + noteId.ToString("N");
        var task = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C1773 park", Goal = "park follow-up",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = session, CreatedAt = now,
        };
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = taskId, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = session, RequestedAt = now, LastEvaluatedAt = now, LastProgressAt = now,
            State = LandRequestState.Completed, IsPending = false,
        };
        var terminal = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, LandRequestId = request.Id, IsLandTerminal = true,
            Type = AgentTaskEventType.LandRefused, At = now, Detail = "c1773",
        };
        request.TerminalEventId = terminal.Id;
        task.CurrentLandRequestId = request.Id;
        db.AgentTasks.Add(task);
        db.AgentTaskLandRequests.Add(request);
        db.AgentTaskEvents.Add(terminal);
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId, RequestId = request.Id, TaskId = taskId, SourceEventId = terminal.Id,
            Kind = LandNotificationKind.Outcome, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = session, Body = body, ContentDigest = noteId.ToString("N"),
            CreatedAt = now, NextAttemptAt = now.AddMinutes(-1), State = state,
            QueueMessageId = messageId, EnqueuedAt = now,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = messageId, AgentSessionId = session, Body = body, Status = QueuedMessageStatus.Pending,
            Sequence = _sequence++, Origin = QueuedMessageOrigin.Delegation, CreatedAt = now,
            SourceLandNotificationId = noteId, SourceTaskId = taskId, ContentDigest = noteId.ToString("N"),
            DeliveryAttempts = attempts,
            LastDeliveryBaselineSequence = attempts > 0 ? 0 : null,
            LastDeliveryStartedAt = attempts > 0 ? now.AddSeconds(-1) : null,
        });
        await db.SaveChangesAsync();
        return new Seeded(noteId, messageId, session, body);
    }

    private static async Task<Guid> CopySessionAsync()
    {
        await using var db = new AppDbContext(_options);
        var template = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == _harness.SessionId);
        var id = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = template.DefinitionName,
            AgentKind = template.AgentKind,
            SessionBackend = template.SessionBackend,
            Status = SessionStatus.Running,
            Cwd = template.Cwd,
            Cols = template.Cols,
            Rows = template.Rows,
            CreatedAt = template.CreatedAt,
            StartedAt = template.StartedAt,
            LastSeenAt = template.LastSeenAt,
            TerminationSource = template.TerminationSource,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static TranscriptEntry Prompt(Guid sessionId, string body)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1,
            Kind = TranscriptKinds.UserPrompt, Text = body, CreatedAt = now, Timestamp = now,
        };
    }

    private static async Task<Guid> TokenAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.Id == id).Select(n => n.ConcurrencyToken).SingleAsync();
    }

    private static async Task<string?> ErrorAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.Id == id).Select(n => n.LastErrorCode).SingleAsync();
    }

    private static async Task<LandNotificationState> StateAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.Id == id).Select(n => n.State).SingleAsync();
    }

    private static async Task<DateTime?> ConfirmedAtAsync(Guid id)
    {
        await using var db = new AppDbContext(_options);
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.Id == id).Select(n => n.ConfirmedAt).SingleAsync();
    }

    private readonly record struct Seeded(Guid NoteId, Guid MessageId, Guid SessionId, string Body);

    private sealed class StampObserver : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private readonly List<Guid> _sessions = [];

        public void Reset()
        {
            lock (_gate) _sessions.Clear();
        }

        public bool SawSession(Guid sessionId)
        {
            lock (_gate) return _sessions.Contains(sessionId);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Observe(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Observe(DbCommand command)
        {
            var sql = command.CommandText;
            if (!sql.Contains("AgentSessions", StringComparison.Ordinal)
                && !sql.Contains("TranscriptEntries", StringComparison.Ordinal))
                return;
            if (sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
                return;
            var ids = new List<Guid>();
            foreach (DbParameter parameter in command.Parameters)
                Collect(parameter.Value, ids);
            if (ids.Count == 0)
                return;
            lock (_gate) _sessions.AddRange(ids);
        }

        private static void Collect(object? value, List<Guid> ids)
        {
            switch (value)
            {
                case Guid id:
                    ids.Add(id);
                    break;
                case string text when Guid.TryParse(text, out var parsed):
                    ids.Add(parsed);
                    break;
                case IEnumerable<Guid> many:
                    ids.AddRange(many);
                    break;
            }
        }
    }
}

using System.Data.Common;
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
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1769. A hosted pass over a seeded set of AwaitingReceipt notes counts statements and
/// UPDATEs. The next pass, with the target session unchanged, must not update them. A receipt
/// prompt, a session status change, or a generation change reconciles that note again. A status
/// change that lands after a note's reconcile and before the pass parks it is reconciled on the
/// next pass. A parked note's queue delivery or cancel reconciles it on the next pass. Canceled
/// is absent from the page. The cap revisits one parked note when its backoff is expired.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("card-1769-land-note-rescan")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandNotificationRescanTests
{
    private const int Population = 1500;

    private static IsolatedTestSchema _schema = null!;
    private static BridgeQueueHarness _harness = null!;
    private static DbContextOptions<AppDbContext> _options = null!;
    private static ScanObserver _observer = null!;
    private static Guid _canceledId;
    private static Guid _statusNoteId;
    private static Guid _statusSessionId;
    private static Guid _promptNoteId;
    private static Guid _promptSessionId;
    private static Guid _generationNoteId;
    private static Guid _generationSessionId;
    private static Guid _exhaustedNoteId;
    private static Guid _capNoteId;
    private static Guid _deliveryNoteId;
    private static Guid _cancelNoteId;
    private static Guid _midPassNoteId;
    private static Guid _midPassSessionId;
    private static readonly MidPassBoundary _boundary = new();
    private static Guid[] _sampleIds = null!;

    [Before(Class), Timeout(180_000)]
    public static async Task Start()
    {
        _schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        _observer = new ScanObserver();
        _harness = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = _schema.ConnectionString,
            ConfigureDbContext = options => options.AddInterceptors(_observer),
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
                services.AddSingleton<LandDeliveryBoundary>(_boundary);
            },
        });
        _options = TestDbFixture.CreateDbContextOptions(_schema.ConnectionString, _observer);
        await SeedAsync();
    }

    [After(Class)]
    public static async Task Stop()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
        if (_schema is not null)
            await _schema.DisposeAsync();
    }

    [Test, Timeout(600_000)]
    public async Task C1769_BoundRescanOfAwaitingReceiptNotes(CancellationToken cancellationToken)
    {
        var worker = new AgentTaskLandNotificationHostedService(
            _harness.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        _observer.Reset();
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        var before = _observer.Snapshot();
        var tokens = await TokensAsync();
        var exhaustedError = await ErrorAsync(_exhaustedNoteId);

        _observer.Reset();
        await worker.ScanLandNotificationsOnceAsync(cancellationToken);
        var after = _observer.Snapshot();
        var quiet = await TokensAsync();

        var measure = $"CARD-1769 measure beforeCommands={before.Commands} beforeUpdates={before.NotificationUpdates} afterCommands={after.Commands} afterUpdates={after.NotificationUpdates}";
        Console.WriteLine(measure);

        var failures = new List<string>();
        if (before.NotificationUpdates < Population)
            failures.Add($"first scan updated {before.NotificationUpdates}, expected at least {Population}");
        if (after.NotificationUpdates != 0)
            failures.Add($"quiet scan updated {after.NotificationUpdates}");
        if (before.Commands <= after.Commands * 10)
            failures.Add($"quiet commands {after.Commands} were not under a tenth of the first scan's {before.Commands}");
        if (_observer.NoteSelects(_canceledId) != 0)
            failures.Add($"canceled note was loaded on the quiet scan ({_observer.NoteSelects(_canceledId)})");
        if (before.NoteSelects(_canceledId) != 0)
            failures.Add($"canceled note was loaded on the first scan ({before.NoteSelects(_canceledId)})");
        foreach (var id in _sampleIds.Append(_exhaustedNoteId).Append(_capNoteId)
                     .Append(_deliveryNoteId).Append(_cancelNoteId).Append(_midPassNoteId))
        {
            if (quiet[id] != tokens[id])
                failures.Add($"quiet scan changed {id}");
        }
        if (exhaustedError != "queue_parked_unconfirmed")
            failures.Add($"exhausted note error was {exhaustedError ?? "null"}");

        if (failures.Count == 0)
        {
            await using (var db = new AppDbContext(_options))
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == _statusSessionId, cancellationToken);
                session.Status = SessionStatus.Stopped;
                await db.SaveChangesAsync(cancellationToken);
            }
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            var woken = await TokensAsync();
            if (woken[_statusNoteId] == tokens[_statusNoteId])
                failures.Add("status change did not reconcile the parked note");
            if (_sampleIds.Any(id => woken[id] != tokens[id]))
                failures.Add("status change reconciled a note on an unchanged session");

            await using (var db = new AppDbContext(_options))
            {
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = _promptSessionId, Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt, Text = "c1769-receipt-wake",
                    CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            var beforePrompt = woken;
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            if (woken[_promptNoteId] == beforePrompt[_promptNoteId])
                failures.Add("new prompt did not reconcile the parked note");
            if (_sampleIds.Any(id => woken[id] != beforePrompt[id]))
                failures.Add("new prompt reconciled a note on an unchanged session");

            await using (var db = new AppDbContext(_options))
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == _generationSessionId, cancellationToken);
                session.StartedAt = session.StartedAt.AddHours(1);
                await db.SaveChangesAsync(cancellationToken);
            }
            var beforeGeneration = woken;
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            if (woken[_generationNoteId] == beforeGeneration[_generationNoteId])
                failures.Add("generation change did not reconcile the parked note");
            if (_sampleIds.Any(id => woken[id] != beforeGeneration[id]))
                failures.Add("generation change reconciled a note on an unchanged session");

            var beforeCap = woken[_capNoteId];
            worker.ExpireRescanPark(_capNoteId);
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            if (woken[_capNoteId] == beforeCap)
                failures.Add("expired backoff did not reconcile the parked note");
            if (_sampleIds.Any(id => woken[id] != quiet[id]))
                failures.Add("expiring one note reconciled a note that was still inside its backoff");

            await using (var db = new AppDbContext(_options))
            {
                var message = await db.SessionQueuedMessages.SingleAsync(
                    m => m.SourceLandNotificationId == _deliveryNoteId, cancellationToken);
                message.Status = QueuedMessageStatus.Sent;
                message.DeliveryAttempts = 1;
                // A Sent row records a delivery floor. Without one, receipt lookup returns before saving.
                message.LastDeliveryBaselineSequence = 0;
                await db.SaveChangesAsync(cancellationToken);
            }
            var beforeDelivery = woken;
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            if (woken[_deliveryNoteId] == beforeDelivery[_deliveryNoteId])
                failures.Add("queue delivery did not reconcile the parked note");
            if (_sampleIds.Any(id => woken[id] != beforeDelivery[id]))
                failures.Add("queue delivery reconciled a note whose queue row was unchanged");
            if (await StateAsync(_deliveryNoteId) != LandNotificationState.AwaitingReceipt)
                failures.Add("queue delivery changed the note state to " + await StateAsync(_deliveryNoteId));

            await using (var db = new AppDbContext(_options))
            {
                var message = await db.SessionQueuedMessages.SingleAsync(
                    m => m.SourceLandNotificationId == _cancelNoteId, cancellationToken);
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            var beforeCancel = woken;
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            if (woken[_cancelNoteId] == beforeCancel[_cancelNoteId])
                failures.Add("queue cancel did not reconcile the parked note");
            if (_sampleIds.Any(id => woken[id] != beforeCancel[id]))
                failures.Add("queue cancel reconciled a note whose queue row was unchanged");
            if (await StateAsync(_cancelNoteId) != LandNotificationState.Canceled)
                failures.Add("queue cancel left the note " + await StateAsync(_cancelNoteId));

            var midBefore = woken[_midPassNoteId];
            var midFired = 0;
            _boundary.Arm(_midPassNoteId, async (_, ct) =>
            {
                midFired++;
                await using var db = new AppDbContext(_options);
                var session = await db.AgentSessions.SingleAsync(s => s.Id == _midPassSessionId, ct);
                session.Status = SessionStatus.Stopped;
                await db.SaveChangesAsync(ct);
            });
            worker.ExpireRescanPark(_midPassNoteId);
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            _boundary.Disarm();
            if (midFired != 1)
                failures.Add($"mid-pass boundary fired {midFired} times");
            if (woken[_midPassNoteId] == midBefore)
                failures.Add("expiring the mid-pass note did not reconcile it");
            var beforeFollowUp = woken[_midPassNoteId];
            var samplesAtFollowUp = woken;
            _observer.Reset();
            await worker.ScanLandNotificationsOnceAsync(cancellationToken);
            woken = await TokensAsync();
            if (woken[_midPassNoteId] == beforeFollowUp)
                failures.Add("status change landed during the pass was absorbed and the next pass skipped the note");
            if (_sampleIds.Any(id => woken[id] != samplesAtFollowUp[id]))
                failures.Add("mid-pass follow-up reconciled a note on an unchanged session");
        }

        failures.ShouldBeEmpty(measure + " | " + string.Join("; ", failures));
        (await StateAsync(_canceledId)).ShouldBe(LandNotificationState.Canceled);
    }

    private static async Task<Dictionary<Guid, Guid>> TokensAsync()
    {
        var ids = _sampleIds.Append(_statusNoteId).Append(_promptNoteId).Append(_generationNoteId)
            .Append(_exhaustedNoteId).Append(_capNoteId).Append(_canceledId)
            .Append(_deliveryNoteId).Append(_cancelNoteId).Append(_midPassNoteId).ToArray();
        await using var db = new AppDbContext(_options);
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.ConcurrencyToken);
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

    private static async Task SeedAsync()
    {
        await using var db = new AppDbContext(_options);
        var template = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == _harness.SessionId);
        _statusSessionId = AddSession(db, template);
        _promptSessionId = AddSession(db, template);
        _generationSessionId = AddSession(db, template);
        var exhaustedSession = AddSession(db, template);
        var capSession = AddSession(db, template);
        var deliverySession = AddSession(db, template);
        var cancelSession = AddSession(db, template);
        _midPassSessionId = AddSession(db, template);
        await db.SaveChangesAsync();

        _sampleIds = new Guid[3];
        var sequence = 1L;
        var batch = new List<Guid>(200);
        for (var i = 0; i < Population; i++)
        {
            var id = AddAwaiting(db, _harness.SessionId, ref sequence, deliveryAttempts: 0);
            if (i < _sampleIds.Length) _sampleIds[i] = id;
            batch.Add(id);
            if (batch.Count == 200)
            {
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        _canceledId = AddAwaiting(db, _harness.SessionId, ref sequence, deliveryAttempts: 0, state: LandNotificationState.Canceled);
        _statusNoteId = AddAwaiting(db, _statusSessionId, ref sequence, deliveryAttempts: 0);
        _promptNoteId = AddAwaiting(db, _promptSessionId, ref sequence, deliveryAttempts: 0);
        _generationNoteId = AddAwaiting(db, _generationSessionId, ref sequence, deliveryAttempts: 0);
        _exhaustedNoteId = AddAwaiting(db, exhaustedSession, ref sequence, deliveryAttempts: 3);
        _capNoteId = AddAwaiting(db, capSession, ref sequence, deliveryAttempts: 0);
        _deliveryNoteId = AddAwaiting(db, deliverySession, ref sequence, deliveryAttempts: 0);
        _cancelNoteId = AddAwaiting(db, cancelSession, ref sequence, deliveryAttempts: 0);
        _midPassNoteId = AddAwaiting(db, _midPassSessionId, ref sequence, deliveryAttempts: 0);
        await db.SaveChangesAsync();
    }

    private static Guid AddSession(AppDbContext db, AgentSession template)
    {
        var id = Guid.NewGuid();
        var copy = new AgentSession
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
        };
        db.AgentSessions.Add(copy);
        return id;
    }

    private static Guid AddAwaiting(AppDbContext db, Guid sessionId, ref long sequence, int deliveryAttempts,
        LandNotificationState state = LandNotificationState.AwaitingReceipt)
    {
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C1769 rescan", Goal = "rescan fixture",
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = sessionId, CreatedAt = now,
        };
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = taskId, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = sessionId, RequestedAt = now, LastEvaluatedAt = now, LastProgressAt = now,
            State = LandRequestState.Completed, IsPending = false,
        };
        var terminal = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, LandRequestId = request.Id, IsLandTerminal = true,
            Type = AgentTaskEventType.LandRefused, At = now, Detail = "c1769",
        };
        request.TerminalEventId = terminal.Id;
        task.CurrentLandRequestId = request.Id;
        var noteId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var body = "c1769-" + noteId.ToString("N");
        var note = new AgentTaskLandNotification
        {
            Id = noteId, RequestId = request.Id, TaskId = taskId, SourceEventId = terminal.Id,
            Kind = LandNotificationKind.Outcome, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = sessionId, Body = body, ContentDigest = noteId.ToString("N"),
            CreatedAt = now, NextAttemptAt = now.AddMinutes(-1), State = state,
            QueueMessageId = messageId, EnqueuedAt = now,
        };
        var message = new SessionQueuedMessage
        {
            Id = messageId, AgentSessionId = sessionId, Body = body, Status = QueuedMessageStatus.Pending,
            Sequence = sequence++, Origin = QueuedMessageOrigin.Delegation, CreatedAt = now,
            SourceLandNotificationId = noteId, SourceTaskId = taskId, ContentDigest = note.ContentDigest,
            DeliveryAttempts = deliveryAttempts,
            LastDeliveryBaselineSequence = deliveryAttempts > 0 ? 0 : null,
            LastDeliveryStartedAt = deliveryAttempts > 0 ? now.AddSeconds(-1) : null,
        };
        db.AgentTasks.Add(task);
        db.AgentTaskLandRequests.Add(request);
        db.AgentTaskEvents.Add(terminal);
        db.AgentTaskLandNotifications.Add(note);
        db.SessionQueuedMessages.Add(message);
        return noteId;
    }

    private sealed class ScanObserver : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private readonly List<Hit> _hits = [];

        public void Reset()
        {
            lock (_gate) _hits.Clear();
        }

        public Shot Snapshot()
        {
            lock (_gate)
            {
                var selects = new Dictionary<Guid, int>();
                foreach (var hit in _hits.Where(hit => hit.NoteSelect))
                {
                    foreach (var id in hit.Ids)
                        selects[id] = selects.GetValueOrDefault(id) + 1;
                }
                return new Shot(_hits.Count, _hits.Count(hit => hit.NotificationUpdate), selects);
            }
        }

        public int NoteSelects(Guid id)
        {
            lock (_gate)
                return _hits.Count(hit => hit.NoteSelect && hit.Ids.Contains(id));
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

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Observe(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Observe(DbCommand command)
        {
            var sql = command.CommandText;
            var ids = new List<Guid>();
            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is Guid id)
                    ids.Add(id);
                else if (parameter.Value is string text && Guid.TryParse(text, out var parsed))
                    ids.Add(parsed);
            }
            var update = sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("AgentTaskLandNotifications", StringComparison.Ordinal);
            var select = !update && sql.Contains("AgentTaskLandNotifications", StringComparison.Ordinal)
                && !sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && !sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase);
            lock (_gate)
                _hits.Add(new Hit(update, select, ids));
        }

        private readonly record struct Hit(bool NotificationUpdate, bool NoteSelect, List<Guid> Ids);

        public sealed class Shot(int commands, int notificationUpdates, Dictionary<Guid, int> selects)
        {
            public int Commands { get; } = commands;
            public int NotificationUpdates { get; } = notificationUpdates;
            public int NoteSelects(Guid id) => selects.GetValueOrDefault(id);
        }
    }

    private sealed class MidPassBoundary : LandDeliveryBoundary
    {
        private Guid _noteId;
        private Func<Guid, CancellationToken, Task>? _mutate;

        public void Arm(Guid noteId, Func<Guid, CancellationToken, Task> mutate)
        {
            _noteId = noteId;
            _mutate = mutate;
        }

        public void Disarm() => _mutate = null;

        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != AgentTaskLandNotificationHostedService.RescanReconciledBoundary
                || taskId != _noteId || _mutate is not { } mutate)
                return;
            _mutate = null;
            await mutate(identity, ct);
        }
    }
}

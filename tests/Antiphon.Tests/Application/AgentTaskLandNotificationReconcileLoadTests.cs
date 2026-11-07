using System.Data.Common;
using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1759. A fresh reconcile reads the note once through a compiled query, and the hosted
/// scan does not load a null-queue note whose next attempt is still in the future.
/// </summary>
[Category("Integration")]
[NotInParallel("card-1759-land-note-load")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandNotificationReconcileLoadTests
{
    private static IsolatedTestSchema _schema = null!;
    private static BridgeQueueHarness _harness = null!;
    private static DbContextOptions<AppDbContext> _options = null!;
    private static LoadObserver _observer = null!;

    [Before(Class)]
    public static async Task Start()
    {
        _schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        _observer = new LoadObserver();
        _harness = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = _schema.ConnectionString,
            ConfigureDbContext = options => options.AddInterceptors(_observer),
            ConfigureServices = services =>
            {
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddScoped<AgentTaskLandNotificationService>();
            },
        });
        _options = TestDbFixture.CreateDbContextOptions(_schema.ConnectionString, _observer);
    }

    [After(Class)]
    public static async Task Stop()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
        if (_schema is not null)
            await _schema.DisposeAsync();
    }

    [Test]
    public async Task C1759_FreshContextLoadsTheNoteOnceThroughACompiledQuery()
    {
        var noteId = await SeedAsync(nextAttemptAt: DateTime.UtcNow.AddHours(2));
        _observer.Reset();
        await using var fresh = new AppDbContext(_options);
        await Service(fresh).ReconcileAsync(noteId, CancellationToken.None);

        var loads = _observer.NoteSelects(noteId);
        loads.Count.ShouldBe(1, _observer.Describe(noteId));
        loads.ShouldAllBe(load => load.Compiled, _observer.Describe(noteId));
    }

    [Test]
    public async Task C1759_MissingDestinationSessionLookupIsCompiled()
    {
        var missingSession = Guid.NewGuid();
        var noteId = await SeedAsync(nextAttemptAt: DateTime.UtcNow.AddMinutes(-1), parentSessionId: missingSession);
        _observer.Reset();
        await using var fresh = new AppDbContext(_options);
        await Service(fresh).ReconcileAsync(noteId, CancellationToken.None);

        var loads = _observer.NoteSelects(noteId);
        loads.Count.ShouldBe(1, _observer.Describe(noteId));
        loads.ShouldAllBe(load => load.Compiled, _observer.Describe(noteId));
        var sessions = _observer.SessionSelects(missingSession);
        sessions.Count.ShouldBeGreaterThan(0, _observer.Describe(missingSession));
        sessions.ShouldAllBe(load => load.Compiled, _observer.Describe(missingSession));

        await using var check = new AppDbContext(_options);
        var saved = await check.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
        saved.State.ShouldBe(LandNotificationState.DestinationUnavailable);
        saved.LastErrorCode.ShouldBe("destination_unavailable");
        saved.QueueMessageId.ShouldBeNull();
    }

    [Test]
    public async Task C1759_TrackedStaleTokenRefreshesInPlace()
    {
        await using var tracked = new AppDbContext(_options);
        var note = await AgentTaskLandReceiptTests.SeedAsync(tracked, _harness.SessionId);
        var staleToken = note.ConcurrencyToken;
        await using (var other = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString)))
        {
            var row = await other.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
            row.ConcurrencyToken = Guid.NewGuid();
            await other.SaveChangesAsync();
        }

        _observer.Reset();
        await Service(tracked).ReconcileAsync(note.Id, CancellationToken.None);

        note.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        note.LastErrorCode.ShouldBeNull();
        note.QueueMessageId.ShouldNotBeNull();
        note.ConcurrencyToken.ShouldNotBe(staleToken);
        _observer.NoteSelects(note.Id).Count.ShouldBe(1, _observer.Describe(note.Id));
    }

    [Test]
    public async Task C1759_HostedScanSkipsUndueUnlinkedNotes()
    {
        var dueId = await SeedAsync(nextAttemptAt: DateTime.UtcNow.AddMinutes(-1));
        var futureId = await SeedAsync(nextAttemptAt: DateTime.UtcNow.AddHours(2));
        var linkedId = await SeedAsync(nextAttemptAt: DateTime.UtcNow.AddMinutes(-1));
        var providerId = await SeedAsync(nextAttemptAt: DateTime.UtcNow.AddHours(2));
        await using (var db = new AppDbContext(_options))
            await Service(db).ReconcileAsync(linkedId, CancellationToken.None);
        await using (var db = new AppDbContext(_options))
        {
            var linked = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == linkedId);
            linked.QueueMessageId.ShouldNotBeNull();
            linked.NextAttemptAt = DateTime.UtcNow.AddHours(2);
            var provider = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == providerId);
            provider.Kind = LandNotificationKind.ProviderInputWait;
            provider.ReplyTo = AgentTaskReplyTo.None;
            provider.NextAttemptAt = DateTime.UtcNow.AddHours(2);
            provider.State = LandNotificationState.Queued;
            provider.QueueMessageId = null;
            await db.SaveChangesAsync();
        }
        _observer.Reset();
        var worker = new AgentTaskLandNotificationHostedService(
            _harness.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await worker.StartAsync(stop.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await using var db = new AppDbContext(_options);
                var dueQueued = await db.AgentTaskLandNotifications.AsNoTracking()
                    .AnyAsync(n => n.Id == dueId && n.QueueMessageId != null);
                var providerSettled = await db.AgentTaskLandNotifications.AsNoTracking()
                    .AnyAsync(n => n.Id == providerId && n.State == LandNotificationState.NotRequired);
                if (dueQueued && providerSettled)
                    break;
                await Task.Delay(100, stop.Token);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }

        _observer.NoteSelects(futureId).Count.ShouldBe(0, "undue null-queue note was loaded: " + _observer.Describe(futureId));
        _observer.NoteSelects(linkedId).Count.ShouldBeGreaterThan(0, "linked undue note was not loaded: " + _observer.Describe(linkedId));
        _observer.NoteSelects(dueId).Count.ShouldBeGreaterThan(0, _observer.Describe(dueId));
        await using var check = new AppDbContext(_options);
        (await check.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == futureId)).State
            .ShouldBe(LandNotificationState.Queued);
        (await check.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == futureId)).QueueMessageId
            .ShouldBeNull();
        (await check.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == dueId)).QueueMessageId
            .ShouldNotBeNull();
        (await check.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == providerId)).State
            .ShouldBe(LandNotificationState.NotRequired);
        (await check.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == linkedId)).QueueMessageId
            .ShouldNotBeNull();
    }

    private static AgentTaskLandNotificationService Service(AppDbContext db) =>
        new(db, _harness.Queue, new CompletionNoteFlushQueue(), _harness.Runtime, TimeProvider.System);

    private static async Task<Guid> SeedAsync(DateTime nextAttemptAt, Guid? parentSessionId = null)
    {
        await using var db = new AppDbContext(_options);
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, _harness.SessionId);
        note.NextAttemptAt = nextAttemptAt;
        if (parentSessionId is Guid parent)
            note.ParentSessionId = parent;
        await db.SaveChangesAsync();
        return note.Id;
    }

    private sealed class LoadObserver : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private readonly List<Select> _selects = [];

        public void Reset()
        {
            lock (_gate)
                _selects.Clear();
        }

        public IReadOnlyList<Select> NoteSelects(Guid id) => Selects("AgentTaskLandNotifications", id);

        public IReadOnlyList<Select> SessionSelects(Guid id) => Selects("AgentSessions", id);

        public string Describe(Guid id)
        {
            lock (_gate)
            {
                var rows = _selects.Where(row => row.Ids.Contains(id) || row.Table.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (rows.Length == 0)
                    rows = _selects.ToArray();
                return rows.Length == 0
                    ? "no selects observed"
                    : string.Join(" || ", rows.Select(row => row.ToString()));
            }
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

        private IReadOnlyList<Select> Selects(string table, Guid id)
        {
            lock (_gate)
                return _selects.Where(row => row.Table == table && row.Ids.Contains(id)).ToArray();
        }

        private void Observe(DbCommand command)
        {
            var sql = command.CommandText;
            if (sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
                return;
            string? table = sql.Contains("AgentTaskLandNotifications", StringComparison.Ordinal) ? "AgentTaskLandNotifications"
                : sql.Contains("\"AgentSessions\"", StringComparison.Ordinal) || sql.Contains("AgentSessions", StringComparison.Ordinal) ? "AgentSessions"
                : null;
            if (table is null)
                return;
            var ids = new List<Guid>();
            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is Guid id)
                    ids.Add(id);
                else if (parameter.Value is string text && Guid.TryParse(text, out var parsed))
                    ids.Add(parsed);
            }
            var compiled = new StackTrace(false).ToString().Contains("CompiledAsyncQuery", StringComparison.Ordinal);
            var compact = sql.Length <= 240 ? sql : sql[..240];
            lock (_gate)
                _selects.Add(new Select(table, ids, compiled, compact));
        }
    }

    private sealed record Select(string Table, List<Guid> Ids, bool Compiled, string Sql)
    {
        public override string ToString() =>
            $"{Table} compiled={Compiled} ids={string.Join(",", Ids)} sql={Sql.Replace('\n', ' ')}";
    }
}

using System.Collections.Concurrent;
using System.Net;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1121 S2. A <see cref="BridgeQueueHarness"/> with the runtime-owned <see cref="SessionStateStore"/>
/// registered, a scripted runner transcript, and production command accounting on every context.
/// Transcript rows reach the database only through <see cref="AgentSessionRuntime.PersistTranscriptAsync"/>
/// (fixture SQL would leave the store at Count 0 while the table holds rows), and the adapter's
/// submissions are ingested the same way, so a real queue delivery publishes through the store as
/// production's live stream does.
/// </summary>
internal sealed class LandReceiptScanHarness : IAsyncDisposable
{
    public const long Floor = 10;

    private readonly List<BridgeQueueHarness> _bridges = [];
    private readonly Options _options;
    private IsolatedTestSchema _schema = null!;
    private int _uuid;

    public sealed record Options
    {
        public bool Store { get; init; } = true;
        public bool StoreEnabled { get; init; } = true;
        public bool Cache { get; init; } = true;
        public TimeProvider? CacheClock { get; init; }
        public LandDeliveryBoundary? Boundary { get; init; }
    }

    private LandReceiptScanHarness(Options options) => _options = options;

    public BridgeQueueHarness Bridge => _bridges[^1];
    public Guid SessionId => _bridges[0].SessionId;
    public AgentSessionRuntime Runtime => Bridge.Runtime;
    public SessionStateStore? Store => Bridge.Provider.GetService<SessionStateStore>();
    public LandReceiptScanCache? Cache { get; private set; }
    public ScriptedTranscriptRunner Runner { get; } = new();
    public FullCommandCounter Commands { get; } = new();
    public ReceiptScanRecognizer Receipts { get; } = new();
    public ArmedTranscriptSaveFault SaveFault { get; } = new();
    public RejectTranscriptUuid Reject { get; } = new();
    public HeldTranscriptSave IngestGate { get; } = new();
    public CompetingTranscriptInsert Competitor { get; } = new();
    public AppDbContext Db { get; private set; } = null!;
    public AgentTaskLandNotificationService Service { get; private set; } = null!;
    public string ConnectionString => _schema.ConnectionString;

    public static async Task<LandReceiptScanHarness> CreateAsync(Options? options = null)
    {
        var h = new LandReceiptScanHarness(options ?? new Options());
        h._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        try
        {
            h._bridges.Add(await BridgeQueueHarness.CreateAsync(h.BridgeOptions(null, null)));
            h.Bind();
            return h;
        }
        catch
        {
            await h.DisposeAsync();
            throw;
        }
    }

    /// <summary>A process restart: a new provider (runtime, store, cache) over the same database and session.</summary>
    public async Task RestartAsync()
    {
        _bridges.Add(await BridgeQueueHarness.CreateAsync(BridgeOptions(_bridges[0].SessionId, _bridges[0].AgentId)
            with { PreserveDatabaseOnDispose = true }));
        await Db.DisposeAsync();
        Bind();
    }

    private BridgeQueueHarness.HarnessOptions BridgeOptions(Guid? attachSession, Guid? attachAgent) => new()
    {
        AlwaysOn = false,
        ConnectionString = _schema.ConnectionString,
        AttachSessionId = attachSession,
        AttachAgentId = attachAgent,
        ConfigureDbContext = o => o.AddInterceptors(Commands, Receipts, SaveFault, Reject, IngestGate, Competitor),
        ConfigureServices = services =>
        {
            services.AddSingleton<ISessionRunnerClient>(Runner);
            if (!_options.Store) return;
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(
                new SessionStateSettings { Enabled = _options.StoreEnabled }));
            services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
            services.AddSingleton<SessionStateStore>();
        },
    };

    // One service instance per provider, on one counted context, as the hosted reconciler's scope has.
    private void Bind()
    {
        Cache = _options.Cache ? new LandReceiptScanCache(_options.CacheClock ?? TimeProvider.System) : null;
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString))
            .AddInterceptors(Commands, Receipts).Options);
        Service = new AgentTaskLandNotificationService(Db, Bridge.Queue, new CompletionNoteFlushQueue(), Bridge.Runtime,
            TimeProvider.System, _options.Boundary, scanCache: Cache);
        // A submission becomes a committed transcript prompt the way the live stream lands one.
        Bridge.Adapter.OnSubmitted = async submitted =>
            await IngestAsync(SessionId, (TranscriptKinds.UserPrompt, submitted), (TranscriptKinds.TurnEnd, null));
    }

    /// <summary>Uncounted fixture context: setup and assertion reads never enter a measured window.</summary>
    public AppDbContext Fixture() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

    /// <summary>
    /// Counted session projections of the shape the reconciler's parent SELECT and the CARD-1157 D-3
    /// SELECT both have: <c>SELECT a."Status", a."StartedAt" FROM "AgentSessions"</c>.
    /// </summary>
    public int SessionProjections => Commands.Commands.Count(sql =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .StartsWith("SELECT a.\"Status\", a.\"StartedAt\" FROM \"AgentSessions\"", StringComparison.Ordinal));

    public void ResetCounters()
    {
        Commands.Reset();
        Receipts.Reset();
        Runner.ResetPulls();
    }

    /// <summary>Runtime ingest at the next runner sequences; returns the stored sequences.</summary>
    public Task<IReadOnlyList<long>> IngestAsync(Guid session, params (string Kind, string? Text)[] entries) =>
        IngestEventsAsync(session, entries.Select(e => Event(session, null, e.Kind, e.Text)).ToArray());

    public SessionRunnerTranscriptEvent Event(Guid session, long? runnerSequence, string kind, string? text,
        DateTimeOffset? timestamp = null, string? uuid = null) =>
        new(session, runnerSequence ?? 0, kind, uuid ?? NextUuid(), null, timestamp ?? DateTimeOffset.UtcNow,
            kind == TranscriptKinds.UserPrompt || kind == TranscriptKinds.QueuedUserPrompt ? "user" : "assistant",
            text, null, null, null, null, kind == TranscriptKinds.TurnEnd ? "end_turn" : null);

    public string NextUuid() => $"c1121-{Interlocked.Increment(ref _uuid)}-{Guid.NewGuid():N}";

    public async Task<IReadOnlyList<long>> IngestEventsAsync(Guid session, IReadOnlyList<SessionRunnerTranscriptEvent> events)
    {
        await using var db = Fixture();
        var max = await db.TranscriptEntries.Where(t => t.AgentSessionId == session).MaxAsync(t => (long?)t.Sequence) ?? 0;
        // Runner sequences continue the stored ones unless a test chose its own.
        var numbered = events.Select((e, i) => e.Sequence > 0 ? e : e with { Sequence = max + i + 1 }).ToArray();
        await Runtime.PersistTranscriptAsync(session, numbered);
        var uuids = numbered.Select(e => e.Uuid).ToArray();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session && uuids.Contains(t.Uuid))
            .OrderBy(t => t.Sequence).Select(t => t.Sequence).ToListAsync();
    }

    /// <summary>
    /// Today's quiet cohort: one row at the floor (<paramref name="atFloorText"/>), 48 unrelated
    /// UserPrompt candidates above it and a closing TurnEnd, ingested in one batch.
    /// </summary>
    public async Task<IReadOnlyList<SessionRunnerTranscriptEvent>> SeedTranscriptAsync(Guid session, string atFloorText = "below the floor",
        int candidates = 48, Func<long, (string Kind, string Text)>? candidate = null)
    {
        var events = new List<SessionRunnerTranscriptEvent> { Event(session, Floor, TranscriptKinds.UserPrompt, atFloorText) };
        for (var seq = Floor + 1; seq <= Floor + candidates; seq++)
        {
            var (kind, text) = candidate?.Invoke(seq) ?? (TranscriptKinds.UserPrompt, Filler(seq));
            events.Add(Event(session, seq, kind, text));
        }
        events.Add(Event(session, Floor + candidates + 1, TranscriptKinds.TurnEnd, null));
        var stored = await IngestEventsAsync(session, events);
        if (stored.Count != events.Count || stored[0] != Floor)
            throw new InvalidOperationException($"seed stored {stored.Count} of {events.Count} rows from {stored.FirstOrDefault()}");
        return events;
    }

    public static string Filler(long sequence) => $"unrelated filler prompt {sequence}";

    /// <summary>
    /// Fails loudly unless the store already holds what the table holds; a scan over an incoherent
    /// fixture would prove nothing about the cache.
    /// </summary>
    public async Task AssertCoherentAsync(Guid session)
    {
        if (Store is not { Enabled: true } store) return;
        await using var db = Fixture();
        var count = await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == session);
        var snapshot = await store.ReadAsync(session, CancellationToken.None);
        if (snapshot.Count != count)
            throw new InvalidOperationException($"store Count {snapshot.Count} != database {count} for {session}");
    }

    /// <summary>
    /// A linked note: <see cref="AgentTaskLandReceiptTests.SeedAsync"/>, then the real enqueue pass.
    /// <paramref name="beforeEnqueue"/> runs once the note exists and before the enqueue pass; a test
    /// ingests its transcript there.
    /// </summary>
    public async Task<AgentTaskLandNotification> SeedLinkedNoteAsync(string? detail = null,
        Func<AgentTaskLandNotification, Task>? beforeEnqueue = null)
    {
        await using var db = Fixture();
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, SessionId, detail: detail);
        if (beforeEnqueue is not null) await beforeEnqueue(note);
        await Service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        if (note.QueueMessageId is null || note.State != LandNotificationState.AwaitingReceipt)
            throw new InvalidOperationException($"enqueue pass left {note.State} {note.LastErrorCode}");
        return note;
    }

    /// <summary>
    /// The keyed row as one typed attempt above <see cref="Floor"/> in the destination's current generation.
    /// A <paramref name="destination"/> retargets the row first (the CARD-1157 carry), so the recorded
    /// generation is that session's.
    /// </summary>
    public async Task<SessionQueuedMessage> MarkSentAsync(AgentTaskLandNotification note, Action<SessionQueuedMessage>? change = null,
        Guid? destination = null)
    {
        await using var db = Fixture();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        if (destination is Guid carried) row.AgentSessionId = carried;
        var startedAt = await db.AgentSessions.Where(s => s.Id == row.AgentSessionId).Select(s => s.StartedAt).SingleAsync();
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = Floor;
        row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        row.LastDeliveryGeneration = startedAt;
        row.SentAt = row.LastDeliveryStartedAt;
        row.DeliveryVerdict = null;
        change?.Invoke(row);
        await db.SaveChangesAsync();
        return await RowAsync(row.Id); // as stored: timestamps carry the column's precision
    }

    public async Task SetStatusAsync(Guid session, SessionStatus status)
    {
        await using var db = Fixture();
        await db.AgentSessions.Where(s => s.Id == session).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status));
    }

    public async Task UpdateNoteAsync(Guid noteId, Action<AgentTaskLandNotification> change)
    {
        await using var db = Fixture();
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == noteId);
        change(note);
        await db.SaveChangesAsync();
    }

    public async Task UpdateRowAsync(Guid rowId, Action<SessionQueuedMessage> change)
    {
        await using var db = Fixture();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == rowId);
        change(row);
        await db.SaveChangesAsync();
    }

    public async Task<AgentTaskLandNotification> NoteAsync(Guid noteId)
    {
        await using var db = Fixture();
        return await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == noteId);
    }

    public async Task<SessionQueuedMessage> RowAsync(Guid rowId)
    {
        await using var db = Fixture();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == rowId);
    }

    /// <summary>A second Stopped session with its own committed transcript.</summary>
    public Task<Guid> AddStoppedSessionAsync() => AddSessionAsync(SessionStatus.Stopped);

    /// <summary>
    /// Another session in <paramref name="status"/>. CARD-1157 fixtures pass a <paramref name="startedAt"/>
    /// one hour before the harness session's, so a stamp built from the wrong session's generation refuses.
    /// </summary>
    public async Task<Guid> AddSessionAsync(SessionStatus status, DateTime? startedAt = null, Guid? id = null)
    {
        var sessionId = id ?? Guid.NewGuid();
        await using var db = Fixture();
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode, Status = status,
            Cwd = Path.Combine(Bridge.TempRoot, "workspace"), Cols = 120, Rows = 30,
            CreatedAt = now, StartedAt = startedAt ?? now, LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    /// <summary>The stored <c>StartedAt</c> of a session (column precision).</summary>
    public async Task<DateTime> StartedAtAsync(Guid session)
    {
        await using var db = Fixture();
        return await db.AgentSessions.Where(s => s.Id == session).Select(s => s.StartedAt).SingleAsync();
    }

    public async Task<SessionStatus> StatusAsync(Guid session)
    {
        await using var db = Fixture();
        return await db.AgentSessions.Where(s => s.Id == session).Select(s => s.Status).SingleAsync();
    }

    /// <summary>
    /// The CARD-0641 receipt rule evaluated in memory over the committed rows above <paramref name="floor"/>:
    /// the first complete matching prompt of an accepted kind, or null.
    /// </summary>
    public async Task<long?> InMemoryFirstReceiptAsync(Guid session, long floor, bool legacy, LandNotificationKind kind, string expected)
    {
        await using var db = Fixture();
        var rows = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == session && t.Sequence > floor)
            .OrderBy(t => t.Sequence).Select(t => new { t.Sequence, t.Kind, t.Text }).ToListAsync();
        var queued = LandNoteReceipt.AcceptsQueuedPrompt(legacy, kind);
        return rows.FirstOrDefault(r => r.Text is not null
            && (r.Kind == TranscriptKinds.UserPrompt || (queued && r.Kind == TranscriptKinds.QueuedUserPrompt))
            && LandNoteReceipt.IsReceipt(expected, r.Text))?.Sequence;
    }

    public async ValueTask DisposeAsync()
    {
        if (Db is not null) await Db.DisposeAsync();
        for (var i = _bridges.Count - 1; i >= 0; i--) await _bridges[i].DisposeAsync();
        if (_schema is not null) await _schema.DisposeAsync();
    }

    /// <summary>
    /// The runner's transcript route as the reconciler sees it: production's 404 for a session it no
    /// longer holds (the client throws), an empty answer, one already-committed row, or scripted entries.
    /// </summary>
    internal sealed class ScriptedTranscriptRunner : ISessionRunnerClient
    {
        public enum Mode { NotFound, Empty, CommittedRow, Entries }

        private readonly ConcurrentDictionary<Guid, int> _pulls = new();
        public Mode Transcript { get; set; } = Mode.NotFound;
        public string? CommittedUuid { get; set; }
        public Func<Guid, IReadOnlyList<SessionRunnerTranscriptEvent>>? NextEntries { get; set; }
        public int KillCalls;

        public int Pulls(Guid session) => _pulls.GetValueOrDefault(session);
        public int TotalPulls => _pulls.Values.Sum();
        public void ResetPulls() => _pulls.Clear();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
        {
            _pulls.AddOrUpdate(sessionId, 1, (_, n) => n + 1);
            IReadOnlyList<SessionRunnerTranscriptEvent> entries = Transcript switch
            {
                Mode.NotFound => throw new HttpRequestException("runner transcript 404", null, HttpStatusCode.NotFound),
                Mode.Empty => [],
                Mode.CommittedRow => [new SessionRunnerTranscriptEvent(sessionId, 1, TranscriptKinds.UserPrompt, CommittedUuid,
                    null, DateTimeOffset.UtcNow, "user", "already committed", null, null, null, null, null)],
                _ => NextEntries?.Invoke(sessionId) ?? [],
            };
            return Task.FromResult(new SessionRunnerTranscriptDto(sessionId, entries, entries.Count == 0 ? 0 : entries.Max(e => e.Sequence)));
        }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Interlocked.Increment(ref KillCalls);
            throw new NotSupportedException("receipt reconciliation must never kill");
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>While armed, any transcript insert fails before it commits (a retained persist failure).</summary>
    internal sealed class ArmedTranscriptSaveFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("planned transcript save fault");
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Refuses every insert, including the persist stub, of a row whose uuid starts with the prefix.</summary>
    internal sealed class RejectTranscriptUuid : SaveChangesInterceptor
    {
        public const string Prefix = "reject-";

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TranscriptEntry>()
                .Any(e => e.State == EntityState.Added && e.Entity.Uuid?.StartsWith(Prefix, StringComparison.Ordinal) == true))
                throw new DbUpdateException("planned row rejection", new Npgsql.PostgresException("planned", "ERROR", "ERROR", "XX000"));
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// CARD-1121 S3. While armed, the next transcript insert stops inside its SaveChanges, so the runtime
    /// ingest holds the session's state gate across an uncommitted append until the test releases it.
    /// </summary>
    internal sealed class HeldTranscriptSave : SaveChangesInterceptor
    {
        private int _armed;
        public TaskCompletionSource Reached { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm()
        {
            Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _armed, 1);
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any(e => e.State == EntityState.Added)
                && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }
            return result;
        }
    }

    /// <summary>
    /// CARD-1121 repair (Final Review 200e9630 F1). While armed, the next runtime insert of a transcript row
    /// whose uuid starts with <see cref="Prefix"/> is preceded by an independent, committed insert of the
    /// same row, so the runtime's save meets a real 23505, recovers the durable row and reseeds its store
    /// (a NeedsReload persist with no retained failure). Fires once per <see cref="Arm"/>.
    /// </summary>
    internal sealed class CompetingTranscriptInsert : SaveChangesInterceptor
    {
        public const string Prefix = "compete-";
        private int _armed;
        public int Injected;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var row = eventData.Context!.ChangeTracker.Entries<TranscriptEntry>()
                .FirstOrDefault(e => e.State == EntityState.Added && e.Entity.Uuid?.StartsWith(Prefix, StringComparison.Ordinal) == true)?.Entity;
            if (row is null || Interlocked.Exchange(ref _armed, 0) == 0) return result;
            await using (var other = new AppDbContext(TestDbFixture.CreateDbContextOptions(eventData.Context.Database.GetConnectionString()!)))
            {
                other.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId, Sequence = row.Sequence, Kind = row.Kind,
                    Uuid = row.Uuid, Timestamp = row.Timestamp, Role = row.Role, Text = row.Text, StopReason = row.StopReason,
                    CreatedAt = DateTime.UtcNow,
                });
                await other.SaveChangesAsync(cancellationToken);
            }
            Interlocked.Increment(ref Injected);
            return result;
        }
    }

    /// <summary>
    /// CARD-1121 S3. Deterministic cuts at the reconciler's named boundaries: <see cref="Hold"/> parks the
    /// pass at a boundary until <see cref="Cut.Release"/>; <see cref="Fail"/> throws there once.
    /// </summary>
    internal sealed class CutBoundary : LandDeliveryBoundary
    {
        private readonly ConcurrentDictionary<string, Cut> _cuts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Exception> _faults = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _reached = new();

        public IReadOnlyList<string> ReachedNames => _reached.ToArray();

        public Cut Hold(string boundary) => _cuts[boundary] = new Cut();

        public void Fail(string boundary, Exception fault) => _faults[boundary] = fault;

        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            _reached.Enqueue(boundary);
            if (_faults.TryRemove(boundary, out var fault)) throw fault;
            if (_cuts.TryRemove(boundary, out var cut))
            {
                cut.Reached.TrySetResult();
                await cut.Released.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            }
        }

        internal sealed class Cut
        {
            public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Release() => Released.TrySetResult();
        }
    }
}

using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1196. One isolated schema, the real terminal land producer, and a replaceable
/// Bridge queue. The adapter records submitted bytes; it is not a native terminal.
/// </summary>
internal sealed class LandRefusalDeliveryHarness : IAsyncDisposable
{
    private readonly List<string> _tempRoots = [];
    private readonly List<Submission> _submissions = [];
    private readonly object _submitGate = new();
    private ServiceProvider? _notifier;
    private int _disposed;

    private LandRefusalDeliveryHarness(RefusalFault fault)
    {
        Fault = fault;
        Clock = new ControlledTimeProvider(DateTimeOffset.UtcNow);
        Land = new LandingProtocolHarness { Clock = Clock };
    }

    public RefusalFault Fault { get; }
    public ControlledTimeProvider Clock { get; }
    public LandingProtocolHarness Land { get; }
    public BridgeQueueHarness Caller { get; private set; } = null!;
    public CompletionNoteFlushQueue Flushes { get; private set; } = new();
    public Guid SessionId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid OtherSessionId { get; private set; }
    public Guid HistoricalEventId { get; private set; }
    public string HistoricalKey => $"legacy-land:{HistoricalEventId:N}";
    public Produced? Live { get; private set; }
    public TranscriptMode Transcript { get; set; } = TranscriptMode.Normal;
    public IReadOnlyList<Submission> Submissions
    {
        get { lock (_submitGate) return _submissions.ToArray(); }
    }

    public static async Task<LandRefusalDeliveryHarness> CreateAsync(RefusalFault? fault = null)
    {
        var world = new LandRefusalDeliveryHarness(fault ?? new RefusalFault());
        try
        {
            await world.InitializeAsync();
            return world;
        }
        catch
        {
            await world.DisposeAsync();
            throw;
        }
    }

    public async Task<Produced> ProduceAsync()
    {
        await Land.RequestAsync();
        await Land.FailAsync(new IOException("C1196:bounded refusal"));
        await using var db = Land.CreateContext();
        var taskId = Land.Git.TaskId;
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.ReplyTo.ShouldBe(AgentTaskReplyTo.Session);
        task.ParentSessionId.ShouldBe(SessionId);
        var liveEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.LandRefused && e.LandRequestId != null)
            .ToListAsync();
        var source = liveEvents.ShouldHaveSingleItem("C1196:live-terminal");
        source.Id.ShouldNotBe(HistoricalEventId);
        source.At.ShouldBeGreaterThan(
            (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == HistoricalEventId)).At);
        var notes = await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == taskId && !n.IsLegacy && n.Kind == LandNotificationKind.Outcome)
            .ToListAsync();
        notes.Count.ShouldBe(1, "C1196:produced-outcome");
        var note = notes[0];
        note.RequestId.ShouldNotBeNull();
        note.SourceEventId.ShouldBe(source.Id);
        note.SourceEventId.ShouldNotBe(HistoricalEventId);
        note.ParentSessionId.ShouldBe(SessionId);
        note.ReplyTo.ShouldBe(AgentTaskReplyTo.Session);
        note.ConfirmedAt.ShouldBeNull();
        note.QueueMessageId.ShouldBeNull();
        (await db.AgentTaskLandNotifications.CountAsync(n => n.SourceEventId == HistoricalEventId))
            .ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && (e.LandingPublication == LandPublicationOutcome.Landed
                || e.LandingPublication == LandPublicationOutcome.AlreadyPresent)))
            .ShouldBe(0, "C1196:no-publication");
        Fault.NoteId = note.Id;
        var produced = new Produced(taskId, note.RequestId!.Value, source.Id, note.Id, note.Body, note.ContentDigest,
            source.At, SessionId);
        Live = produced;
        return produced;
    }

    public async Task RestartAsync()
    {
        await DisposeNotifierAsync();
        var connection = Caller.ConnectionString;
        await Caller.DisposeAsync();
        await Land.RestartServicesAsync();
        Land.Clock = Clock;
        Caller = await OpenBridgeAsync(connection, SessionId, AgentId);
        Flushes = new CompletionNoteFlushQueue();
        BuildNotifier();
        Land.Messages = Caller.Queue;
    }

    public async Task ReconcileLiveAsync()
    {
        var live = Live ?? throw new InvalidOperationException("C1196:no-produced-note");
        await using var scope = Notifier.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
            .ReconcileAsync(live.NoteId, CancellationToken.None);
    }

    public async Task<bool> ConsumeWakeupAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var id in Flushes.ReadAllAsync(timeout.Token))
            {
                try
                {
                    if (id != SessionId)
                        continue;
                    await Caller.Queue.FlushIfIdleAsync(id, CancellationToken.None);
                    return true;
                }
                finally
                {
                    Flushes.Complete(id);
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }

        return false;
    }

    public Task<int> FlushStrandedAsync() => Caller.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

    public Task MarkWorkingAsync() =>
        Caller.MarkWorkingAsync();

    public Task MarkIdleTurnAsync() =>
        Caller.MarkIdleAsync(stopReason: "end_turn");

    public async Task<bool> IsWorkingAsync()
    {
        await using var db = Land.CreateContext();
        return await SessionMessageQueueService.IsWorkingAsync(db, SessionId, CancellationToken.None);
    }

    public async Task OnTurnEndAsync() =>
        await Caller.Queue.OnTurnEndAsync(SessionId, CancellationToken.None);

    public async Task<IReadOnlyList<string>> ReadHistoryKeysAsync()
    {
        await using var db = Land.CreateContext();
        var service = AttentionServiceTests.BuildService(Caller.Runner, db: db, timeProvider: Clock);
        var feed = await service.GetAsync(CancellationToken.None, includeProgressProbe: false);
        db.ChangeTracker.HasChanges().ShouldBeFalse("C1196:attention-read-clean");
        return feed.Items.Select(i => i.ConditionKey).Where(k => k is not null).Select(k => k!).ToList();
    }

    public async Task<NoteView> ReadNoteAsync(Guid? noteId = null)
    {
        var id = noteId ?? Live?.NoteId ?? throw new InvalidOperationException("C1196:no-note");
        await using var db = Land.CreateContext();
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
        return new NoteView(note.Id, note.State, note.QueueMessageId, note.ConfirmedAt, note.ConfirmingPromptSequence,
            note.NextAttemptAt, note.LastErrorCode, note.EnqueueAttempts, note.Body, note.ContentDigest, note.IsLegacy,
            note.Kind, note.SourceEventId, note.ParentSessionId);
    }

    public async Task<QueueView?> ReadKeyedAsync(Guid? noteId = null)
    {
        var id = noteId ?? Live?.NoteId ?? throw new InvalidOperationException("C1196:no-note");
        await using var db = Land.CreateContext();
        var row = await db.SessionQueuedMessages.AsNoTracking()
            .SingleOrDefaultAsync(m => m.SourceLandNotificationId == id);
        return row is null ? null : MapQueue(row);
    }

    public async Task<int> CountKeyedAsync(Guid? noteId = null)
    {
        var id = noteId ?? Live?.NoteId ?? throw new InvalidOperationException("C1196:no-note");
        await using var db = Land.CreateContext();
        return await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == id);
    }

    public async Task<List<PromptView>> ReadPromptsAsync(Guid sessionId, string? kind = null)
    {
        await using var db = Land.CreateContext();
        var query = db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Text != null);
        if (kind is not null)
            query = query.Where(t => t.Kind == kind);
        var rows = await query.OrderBy(t => t.Sequence).ToListAsync();
        return rows.Select(t => new PromptView(t.Sequence, t.Kind, t.Text!, t.Timestamp)).ToList();
    }

    public async Task<long> MaxSequenceAsync(Guid sessionId)
    {
        await using var db = Land.CreateContext();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
    }

    public async Task PadOtherSessionAsync(int count)
    {
        for (var i = 0; i < count; i++)
            await BridgeQueueHarness.InsertEntryAsync(OtherSessionId, TranscriptKinds.AssistantText, "pad " + i,
                connectionString: Caller.ConnectionString, createdAtUtc: DateTime.UtcNow);
    }

    public async Task AdvanceToPersistedDueAsync()
    {
        var note = await ReadNoteAsync();
        var due = new DateTimeOffset(DateTime.SpecifyKind(note.NextAttemptAt, DateTimeKind.Utc));
        if (due > Clock.GetUtcNow())
            Clock.AdvanceTo(due);
    }

    public void AdvanceTo(DateTime at)
    {
        var target = new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc));
        if (target > Clock.GetUtcNow())
            Clock.AdvanceTo(target);
    }

    public async Task<Guid> SeedHistoricalRefusalAsync(Guid taskId, DateTime at)
    {
        var id = Guid.NewGuid();
        await using var db = Land.CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = id,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.LandRefused,
            Detail = "historical land refused",
            At = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<Guid> SeedOtherTaskAsync(DateTime refusalAt)
    {
        var id = Guid.NewGuid();
        await using var db = Land.CreateContext();
        var owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Land.Git.TaskId);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "C1196 other task",
            Goal = "protected history",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = owner.WorkingDirectory,
            RepoPath = owner.RepoPath,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = SessionId,
            CreatedAt = refusalAt,
            CompletedAt = refusalAt,
        });
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.LandRefused,
            Detail = "other task historical refusal",
            At = refusalAt,
        });
        await db.SaveChangesAsync();
        return (await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == id && e.LandRequestId == null && e.Type == AgentTaskEventType.LandRefused)
            .Select(e => e.Id).SingleAsync());
    }

    public async Task<Guid> SeedLegacyNoteAsync()
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        var noteId = Guid.NewGuid();
        await using var db = Land.CreateContext();
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId,
            IsLegacy = true,
            TaskId = Land.Git.TaskId,
            SourceEventId = HistoricalEventId,
            Kind = LandNotificationKind.Outcome,
            ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = SessionId,
            Body = "legacy historical refusal " + new string('x', 240),
            ContentDigest = noteId.ToString("N"),
            CreatedAt = now,
            NextAttemptAt = now,
            State = LandNotificationState.AwaitingReceipt,
        });
        await db.SaveChangesAsync();
        Live = new Produced(Land.Git.TaskId, Guid.Empty, HistoricalEventId, noteId,
            "legacy historical refusal " + new string('x', 240), noteId.ToString("N"), now, SessionId);
        return noteId;
    }

    public async Task SetQueueFieldsAsync(Action<SessionQueuedMessage> edit)
    {
        var id = Live?.NoteId ?? throw new InvalidOperationException("C1196:no-note");
        await using var db = Land.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == id);
        edit(row);
        await db.SaveChangesAsync();
    }

    public async Task SetNoteKindAsync(LandNotificationKind kind)
    {
        var id = Live?.NoteId ?? throw new InvalidOperationException("C1196:no-note");
        await using var db = Land.CreateContext();
        var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == id);
        note.Kind = kind;
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await DisposeNotifierAsync();
        if (Caller is not null)
            await Caller.DisposeAsync();
        foreach (var root in _tempRoots.Distinct(StringComparer.Ordinal))
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Temp-directory cleanup is best effort; a failed delete is left behind (no retry).
            }
        }

        if (Land.Schema is not null)
            await Land.DisposeAsync();
        else
            Land.Git.Dispose();
    }

    private async Task InitializeAsync()
    {
        await Land.InitializeAsync();
        Caller = await OpenBridgeAsync(Land.Schema.ConnectionString, null, null);
        SessionId = Caller.SessionId;
        AgentId = Caller.AgentId;
        await using (var db = Land.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == Land.Git.TaskId);
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = SessionId;
            HistoricalEventId = Guid.NewGuid();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = HistoricalEventId,
                AgentTaskId = task.Id,
                Type = AgentTaskEventType.LandRefused,
                Detail = "historical land refused",
                At = Clock.GetUtcNow().UtcDateTime.AddDays(-3),
            });
            OtherSessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = OtherSessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Caller.TempRoot,
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        await Caller.InsertTurnAsync("baseline prompt that is long enough to be a real turn", "baseline reply");
        BuildNotifier();
        Land.Messages = Caller.Queue;
    }

    private async Task<BridgeQueueHarness> OpenBridgeAsync(string connection, Guid? sessionId, Guid? agentId)
    {
        var bridge = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = connection,
            AttachSessionId = sessionId,
            AttachAgentId = agentId,
            ConfigureDbContext = Fault.Cut == RefusalCut.QueueSave ? o => o.AddInterceptors(Fault.Interceptor) : null,
            ConfigureServices = services => services.AddSingleton<LandDeliveryBoundary>(Fault.Boundary),
        });
        if (!_tempRoots.Contains(bridge.TempRoot))
            _tempRoots.Add(bridge.TempRoot);
        bridge.Adapter.OnSubmitted = RecordSubmissionAsync;
        return bridge;
    }

    private void BuildNotifier()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Flushes);
        services.AddSingleton(Caller.Queue);
        services.AddSingleton(Caller.Runtime);
        services.AddSingleton<LandDeliveryBoundary>(Fault.Boundary);
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(Land.Schema.ConnectionString)));
        services.AddScoped<AgentTaskLandNotificationService>();
        _notifier = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private ServiceProvider Notifier => _notifier ?? throw new InvalidOperationException("C1196:notifier-not-built");

    private async Task RecordSubmissionAsync(string submitted)
    {
        var live = Live;
        int attempts = 0;
        long? baseline = null;
        DateTime? started = null;
        var found = false;
        if (live is not null)
        {
            await using var db = Land.CreateContext();
            var row = await db.SessionQueuedMessages.AsNoTracking()
                .SingleOrDefaultAsync(m => m.SourceLandNotificationId == live.NoteId);
            if (row is not null)
            {
                found = true;
                attempts = row.DeliveryAttempts;
                baseline = row.LastDeliveryBaselineSequence;
                started = row.LastDeliveryStartedAt;
            }

            var working = await SessionMessageQueueService.IsWorkingAsync(db, SessionId, CancellationToken.None);
            string? insertedKind = null;
            string? insertedText = null;
            if (Transcript != TranscriptMode.Suppress)
            {
                insertedText = Transcript == TranscriptMode.HeadOnly ? TryHeadOnly(live.Body, submitted) : submitted;
                if (insertedText is not null)
                {
                    insertedKind = working ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.UserPrompt;
                    var session = Transcript == TranscriptMode.WrongSession ? OtherSessionId : SessionId;
                    await BridgeQueueHarness.InsertEntryAsync(session, insertedKind, insertedText,
                        timestamp: DateTime.UtcNow, connectionString: Caller.ConnectionString,
                        createdAtUtc: DateTime.UtcNow);
                }
            }

            lock (_submitGate)
            {
                _submissions.Add(new Submission(submitted, attempts, baseline, started, found, working,
                    insertedKind, insertedText));
            }

            return;
        }

        lock (_submitGate)
            _submissions.Add(new Submission(submitted, 0, null, null, false, false, null, null));
    }

    private static string? TryHeadOnly(string expected, string submitted)
    {
        for (var i = 1; i <= submitted.Length; i++)
        {
            var slice = submitted[..i];
            if (PromptSubmissionMatch.IsConfirmedBy(expected, slice)
                && !PromptSubmissionMatch.IsCompleteIn(expected, slice))
                return slice;
        }

        return null;
    }

    private async Task DisposeNotifierAsync()
    {
        if (_notifier is null)
            return;
        await _notifier.DisposeAsync();
        _notifier = null;
    }

    private static QueueView MapQueue(SessionQueuedMessage row) => new(
        row.Id, row.AgentSessionId, row.Status, row.DeliveryAttempts, row.LastDeliveryBaselineSequence,
        row.LastDeliveryStartedAt, row.DeliveryVerdict, row.ContentDigest, row.Body, row.SourceTaskId,
        row.SourceLandNotificationId);

    internal enum TranscriptMode
    {
        Normal,
        HeadOnly,
        WrongSession,
        Suppress,
    }

    internal sealed record Produced(
        Guid TaskId, Guid RequestId, Guid EventId, Guid NoteId, string Body, string Digest, DateTime EventAt,
        Guid SessionId);

    internal sealed record NoteView(
        Guid Id, LandNotificationState State, Guid? QueueMessageId, DateTime? ConfirmedAt, long? ConfirmingSequence,
        DateTime NextAttemptAt, string? LastError, int EnqueueAttempts, string Body, string Digest, bool IsLegacy,
        LandNotificationKind Kind, Guid SourceEventId, Guid? ParentSessionId);

    internal sealed record QueueView(
        Guid Id, Guid SessionId, QueuedMessageStatus Status, int Attempts, long? Baseline, DateTime? StartedAt,
        DeliveryVerdict? Verdict, string? Digest, string Body, Guid? SourceTaskId, Guid? SourceLandNotificationId);

    internal sealed record PromptView(long Sequence, string Kind, string Text, DateTime? Timestamp);

    internal sealed record Submission(
        string Text, int Attempts, long? Baseline, DateTime? StartedAt, bool RowFound, bool Working,
        string? InsertedKind, string? InsertedText);
}

internal enum RefusalCut
{
    None,
    BeforeEnqueue,
    QueueSave,
    QueueInserted,
    LostWakeup,
    QueueBeforeVerdict,
    ReceiptBeforeSave,
    QueueBeforeTyping,
}

internal sealed class RefusalFault
{
    public RefusalFault(RefusalCut cut = RefusalCut.None)
    {
        Cut = cut;
        Armed = cut != RefusalCut.None;
        Boundary = new CutBoundary(this);
        Interceptor = new QueueSaveInterceptor(this);
    }

    public RefusalCut Cut { get; }
    public bool Armed { get; set; }
    public int Fired { get; private set; }
    public Guid NoteId { get; set; }
    public CutBoundary Boundary { get; }
    public QueueSaveInterceptor Interceptor { get; }

    public void MarkFired()
    {
        Fired++;
        Armed = false;
    }
}

internal sealed class CutBoundary(RefusalFault fault) : LandDeliveryBoundary
{
    public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
    {
        if (!fault.Armed || RefusalNames.For(fault.Cut) != boundary)
            return Task.CompletedTask;
        fault.MarkFired();
        if (fault.Cut is RefusalCut.BeforeEnqueue or RefusalCut.QueueBeforeTyping)
            throw new IOException("C1196:" + boundary);
        throw new OperationCanceledException("C1196:" + boundary);
    }
}

internal sealed class QueueSaveInterceptor(RefusalFault fault) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Cut(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Cut(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Cut(DbContext? context)
    {
        if (!fault.Armed || fault.Cut != RefusalCut.QueueSave || context is null || fault.NoteId == Guid.Empty)
            return;
        var added = context.ChangeTracker.Entries<SessionQueuedMessage>().Any(e =>
            e.State == EntityState.Added && e.Entity.SourceLandNotificationId == fault.NoteId);
        if (!added)
            return;
        fault.MarkFired();
        throw new IOException("C1196:queue-save");
    }
}

internal static class RefusalNames
{
    public static string? For(RefusalCut cut) => cut switch
    {
        RefusalCut.BeforeEnqueue => "before-enqueue",
        RefusalCut.QueueInserted => "queue-inserted",
        RefusalCut.LostWakeup => "queue-committed-before-wakeup",
        RefusalCut.QueueBeforeVerdict => "queue-before-verdict",
        RefusalCut.ReceiptBeforeSave => "receipt-before-save",
        RefusalCut.QueueBeforeTyping => "queue-before-typing",
        _ => null,
    };
}

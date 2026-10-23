using System.Net;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

// T37 records actual adapter submissions in runner custody. Receipt ingestion uses
// SessionRunnerHttpClient and AgentSessionRuntime; it never inserts a SQL receipt.
internal sealed class TurnEndGapDeliveryHarness : IAsyncDisposable
{
    public C544World World { get; private set; } = null!;
    public ControlledTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public FakeAgentProtocolAdapter Caller { get; } = new();
    public C544DeliveryFault Fault { get; } = new();
    public DeliveryBoundary Boundary { get; } = new();
    public RecordingFlushes Flushes { get; private set; } = new();
    public Func<string, string>? SubmitTransform { get; set; }
    public Guid? SubmitDestination { get; set; }
    public bool SuppressSubmittedPrompt { get; set; }
    public Func<Task>? AfterSubmitted { get; set; }
    private readonly RecordingLogger<SessionRunnerEventPump> _pumpLog = new();
    private readonly RecordingLogger<AgentSessionRuntime> _runtimeLog = new();
    private readonly RecordingLogger<SessionMessageQueueService> _queueLog = new();
    private readonly RecordingLogger<StaleWorkingTranscriptRecovery> _recoveryLog = new();
    private readonly RunnerHandler _handler = new();
    private readonly HttpClient _http;
    private readonly bool _cache;
    private SessionRunnerEventPump? _pump;
    private CompletionNoteWorkHostedService? _worker;
    private OwnedLoop? _workerLoop, _pumpLoop, _gapActionsLoop;
    private EpochAdapter? _adapter;
    private readonly WorkerScopeGate _workerScopes = new();
    private readonly ContinuationQueue _continuations = new();
    public bool ConcurrentScopeBarriers { get; set; }
    private int _postRestartDeliveryWorkerCycles;
    private bool _restarted;
    public int PostRestartDeliveryWorkerCycles => Volatile.Read(ref _postRestartDeliveryWorkerCycles);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(90));
    public List<Guid> Tasks { get; } = [];
    public Guid Id => World.CallerSessionId;
    public AgentSessionRuntime Runtime => World.Services.GetRequiredService<AgentSessionRuntime>();
    public SessionMessageQueueService Queue => World.Services.GetRequiredService<SessionMessageQueueService>();
    public Task GapCaptured => _handler.GapCaptured.Task;
    public Task PumpDrained => _handler.Drained.Task;
    public CancellationToken Token => _stop.Token;
    public StaleWorkingTranscriptRecovery Recovery => World.Services.GetRequiredService<StaleWorkingTranscriptRecovery>();
    public int TranscriptPulls => _handler.Pulls;
    public bool WrongGeneration { get => _handler.WrongGeneration; set => _handler.WrongGeneration = value; }
    public bool FailPull { get => _handler.FailPull; set => _handler.FailPull = value; }
    public int Keepalives => _handler.Stream?.Keepalives ?? 0;
    public string[] RecoveryOutcomes => _recoveryLog.Entries.Where(e => e.State.ContainsKey("Outcome"))
        .Select(e => e.State["Outcome"]!.ToString()!).ToArray();

    private TurnEndGapDeliveryHarness(bool cache)
    {
        _cache = cache;
        _http = new HttpClient(_handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static async Task<TurnEndGapDeliveryHarness> CreateAsync(bool cache = true, bool busy = true)
    {
        var h = new TurnEndGapDeliveryHarness(cache);
        try
        {
            h.World = await C544World.CreateAsync(h.Fault, configure: h.Configure);
            await using var db = h.World.CreateContext();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.Id);
            h._handler.Id = h.Id;
            h._handler.Generation = session.StartedAt;
            await h.AttachAsync();
            h._handler.Append(TranscriptKinds.UserPrompt, "dispatch five tasks", publish: false);
            await h.Runtime.CatchUpTranscriptAsync(h.Id, h.Token);
            if (!busy) await h.MakeIdleAsync();
            return h;
        }
        catch { await h.DisposeAsync(); throw; }
    }

    private void Configure(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ILogger<SessionRunnerEventPump>>(_pumpLog);
        services.AddSingleton<ILogger<AgentSessionRuntime>>(_runtimeLog);
        services.AddSingleton<ILogger<SessionMessageQueueService>>(_queueLog);
        services.AddSingleton<ILogger<StaleWorkingTranscriptRecovery>>(_recoveryLog);
        services.AddSingleton(Options.Create(new SessionStateSettings { Enabled = _cache }));
        services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
        services.AddSingleton<SessionStateStore>();
        services.AddSingleton<SessionRuntimeActionCoordinator>();
        var settings = Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings
        {
            Enabled = true, BaseUrl = "http://runner.invalid", EventStreamIdleTimeoutSeconds = 600,
            EventReconnectDelayMs = 60000,
        });
        services.AddSingleton(settings);
        services.AddSingleton<ISessionRunnerClient>(new SessionRunnerHttpClient(
            _http, new Factory(_handler), settings, time: Clock));
        services.AddSingleton(Options.Create(new StaleWorkingTranscriptSettings()));
        services.AddSingleton<StaleWorkingTranscriptRecovery>();
        services.AddSingleton<SpecialistFailureQueue>();
        Flushes = new RecordingFlushes();
        services.AddSingleton<CompletionNoteFlushQueue>(Flushes);
        services.AddSingleton<LandDeliveryBoundary>(Boundary);
    }

    private async Task AttachAsync()
    {
        await Caller.EnableClaudeDialogObservationAsync(World.Schema.ConnectionString, Id);
        _adapter = new EpochAdapter(Caller);
        Runtime.Register(Id, _adapter);
        Caller.OnSubmitted = async body =>
        {
            if (!SuppressSubmittedPrompt)
                _handler.Append(TranscriptKinds.UserPrompt, (SubmitTransform?.Invoke(body) ?? body).ReplaceLineEndings("\n"),
                    publish: true, sessionId: SubmitDestination);
            _handler.Append(TranscriptKinds.TurnEnd, "", publish: true);
            if (AfterSubmitted is not null) await AfterSubmitted();
        };
    }

    public async Task ProduceFiveAsync()
    {
        await ProduceAsync(5);
        Tasks.Count.ShouldBe(5);
    }

    public async Task ProduceAsync(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            var created = await World.CreateTaskAsync(World.FinalReview($"Review independent task {i}."));
            Tasks.Add(created.Id);
            var session = await World.DispatchAsync(created.Id);
            await World.SeedTurnAsync(session, created.Id,
                C544World.ReviewReport(created.Id, World.Owner.Id, World.OwnerSha, "Full", false, "review"));
            await World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, Token);
        }
    }

    public async Task AssertHeldAsync()
    {
        var rows = await RowsAsync();
        rows.Count.ShouldBe(Tasks.Count);
        rows.ShouldAllBe(r => r.Status == QueuedMessageStatus.Pending && r.DeliveryAttempts == 0);
        Caller.SubmittedBodies.ShouldBeEmpty();
        await using var db = World.CreateContext();
        (await SessionMessageQueueService.IsWorkingAsync(db, Id, Token)).ShouldBeTrue();
        (await NotesAsync()).ShouldAllBe(n => n.State != LandNotificationState.Confirmed && n.ConfirmingPromptSequence == null);
    }

    public async Task<List<SessionQueuedMessage>> RowsAsync()
    {
        await using var db = World.CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == Id
            && m.SourceTaskId != null && Tasks.Contains(m.SourceTaskId.Value)).OrderBy(m => m.Sequence).ToListAsync(Token);
    }

    public async Task<List<AgentTaskLandNotification>> NotesAsync()
    {
        await using var db = World.CreateContext();
        return await db.AgentTaskLandNotifications.AsNoTracking().Where(n => Tasks.Contains(n.TaskId)
            && n.Kind == LandNotificationKind.TaskCompletion).OrderBy(n => n.TaskId).ToListAsync(Token);
    }

    public async Task RestartAsync()
    {
        Flushes.ReleaseHold.TrySetResult();
        await Recovery.StopAsync(CancellationToken.None);
        await StopWorkerAsync();
        await StopPumpAsync();
        if (_adapter is not null) await _adapter.DisposeAsync();
        await World.RestartAsync(keepInterceptor: true);
        await AttachAsync();
        _restarted = true;
        Interlocked.Exchange(ref _postRestartDeliveryWorkerCycles, 0);
    }

    // Exercise the fresh worker even when boot discovery has no unattempted rows.
    // The existing interrupted-attempt age is 3+3+30 seconds in C544World.
    public async Task RecoverDeliveryAfterRestartAsync()
    {
        Clock.Advance(TimeSpan.FromSeconds(37));
        Flushes.TryEnqueue(Id).ShouldBeTrue();
        await StartCompletionWorkerAsync(minimumScans: 0);
        await JoinOwnedWorkAsync();
    }

    public async Task StartCompletionWorkerAsync(int? minimumScans = null)
    {
        var after = Clock.Events.LastOrDefault()?.Sequence ?? 0;
        _worker = new CompletionNoteWorkHostedService(new ObservedScopes(World.Services.GetRequiredService<IServiceScopeFactory>(), _workerScopes,
            () => { if (_restarted) Interlocked.Increment(ref _postRestartDeliveryWorkerCycles); }),
            Flushes, World.Services.GetRequiredService<SpecialistFailureQueue>(), Clock,
            World.Services.GetRequiredService<ILogger<CompletionNoteWorkHostedService>>());
        _workerLoop = new OwnedLoop(_worker, Token);
        await _workerLoop.StartAsync(ConcurrentScopeBarriers ? null : _continuations);
        // The boot sweep has enumerated its durable work before its 15-minute wait.
        await Clock.WaitForTimerAsync(e => e.Action == "create" && e.DueTime >= TimeSpan.FromMinutes(14), after, Token);
        await JoinDeliveryAsync(WaitForQuiescenceAsync());
        Boundary.Count("completion-scan").ShouldBeGreaterThanOrEqualTo(minimumScans ?? Tasks.Count);
    }

    public Task JoinOwnedWorkAsync() => JoinDeliveryAsync(WaitForQuiescenceAsync());

    private async Task WaitForQuiescenceAsync()
    {
        var flushes = World.Services.GetRequiredService<CompletionNoteFlushQueue>();
        var pending = (System.Collections.IDictionary)typeof(CompletionNoteFlushQueue)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flushes)!;
        var recovery = flushes.Recovery;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var recoveryGate = recovery.GetType().GetField("_gate", flags)!.GetValue(recovery)!;
        var actions = World.Services.GetRequiredService<SessionRuntimeActionCoordinator>();
        var owners = (ConcurrentDictionary<Guid, byte>)typeof(SessionRuntimeActionCoordinator)
            .GetField("_owners", flags)!.GetValue(actions)!;
        var hints = (Channel<Guid>)typeof(SessionRuntimeActionCoordinator)
            .GetField("_hints", flags)!.GetValue(actions)!;
        while (true)
        {
            Token.ThrowIfCancellationRequested();
            bool Quiet()
            {
                var backstop = Clock.Events.LastOrDefault(e => e.Action == "create" && e.DueTime >= TimeSpan.FromMinutes(14));
                var recoveryWaiting = backstop is not null && Clock.TimerStates[backstop.TimerId] != "dispose";
                bool noHints;
                lock (recoveryGate)
                    noHints = !(bool)recovery.GetType().GetField("_sweep", flags)!.GetValue(recovery)!
                        && ((HashSet<Guid>)recovery.GetType().GetField("_tasks", flags)!.GetValue(recovery)!).Count == 0
                        && ((Dictionary<Guid, DateTime>)recovery.GetType().GetField("_sessions", flags)!.GetValue(recovery)!).Count == 0;
                var discovery = Clock.Events.LastOrDefault(e => e.Action == "create" && e.DueTime == TimeSpan.FromSeconds(1));
                var actionsQuiet = _gapActionsLoop is null || (owners.IsEmpty && hints.Reader.Count == 0
                    && discovery is not null && Clock.TimerStates[discovery.TimerId] == "create");
                return pending.Count == 0 && noHints && actionsQuiet && recoveryWaiting
                    && (_pump is null || _handler.Stream?.Quiescent == true);
            }
            if (ConcurrentScopeBarriers)
            { if (Quiet()) return; await Task.Yield(); }
            else
            {
                // Observe between owned continuations, never inside Complete's
                // synchronous remove/re-enqueue publication. No delay is an oracle.
                var observation = await _continuations.ObserveAsync(Quiet);
                if (observation.Quiet)
                {
                    // Releasing the volatile owner does not discharge durable deferred
                    // queue work. Let the real discovery loop reach its next due pass
                    // while idle; busy-tail obligations must remain held.
                    if (_gapActionsLoop is not null && !await WorkingAsync())
                    {
                        await using var db = World.CreateContext();
                        var due = await db.SessionRuntimeActions.Where(a => a.AgentSessionId == Id)
                            .MinAsync(a => (DateTime?)a.DueAt, Token);
                        if (due is not null)
                        {
                            var discovery = Clock.Events.Last(e => e.Action == "create" && e.DueTime == TimeSpan.FromSeconds(1));
                            var deadline = new DateTimeOffset(due.Value, TimeSpan.Zero);
                            if (deadline < discovery.Deadline) deadline = discovery.Deadline;
                            if (deadline > Clock.GetUtcNow()) Clock.AdvanceTo(deadline);
                            continue;
                        }
                    }
                    return;
                }
                await _continuations.WaitForProgressAsync(observation.Sequence, Token);
            }
        }
    }

    public async Task StartGapAsync()
    {
        await StartActionsAsync();
        _handler.ArmGap();
        _pump = ActivatorUtilities.CreateInstance<SessionRunnerEventPump>(World.Services);
        _pumpLoop = new OwnedLoop(_pump, Token);
        await _pumpLoop.StartAsync(_continuations);
        await GapCaptured.WaitAsync(Token);
    }

    private async Task StartActionsAsync()
    {
        if (_gapActionsLoop is not null) return;
        // Sync now admits durable actions instead of running its boundary inline.
        // Own that production processing path for the captured-gap experiment;
        // the explicit-delivery fault-cut cases keep their staged setup unchanged.
        _gapActionsLoop = new OwnedLoop(new SessionRuntimeActionHostedService(
            World.Services.GetRequiredService<SessionRuntimeActionCoordinator>()), Token);
        await _gapActionsLoop.StartAsync(_continuations);
    }

    public void ReleaseGapWithEnd()
    {
        // Snapshot already captured; the real hub has no subscriber yet.
        _handler.Append(TranscriptKinds.TurnEnd, "", publish: true);
        _handler.ReleaseGap.TrySetResult();
    }

    public async Task JoinDeliveryAsync(Task operation)
    {
        var after = 0;
        while (!operation.IsCompleted)
        {
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var timer = Clock.WaitForTimerAsync(e => e.Action == "create"
                && (e.DueTime == TimeSpan.FromMilliseconds(20)
                    || e.DueTime == TimeSpan.FromMilliseconds(50))
                && Clock.TimerStates[e.TimerId] != "dispose", after, pending.Token);
            if (await Task.WhenAny(operation, timer) == operation)
            {
                await pending.CancelAsync();
                try { await timer; } catch (OperationCanceledException) { }
                break;
            }
            var registered = await timer;
            after = registered.Sequence;
            Clock.AdvanceTo(registered.Deadline);
        }
        await operation.WaitAsync(Token);
    }

    public async Task AssertReceiptsAsync(List<AgentTaskLandNotification> before, List<SessionQueuedMessage> queued, int expectedAttempts = 1)
    {
        if (Caller.SubmittedBodies.Count != Tasks.Count)
        {
            await using var evidence = World.CreateContext();
            var ends = await evidence.TranscriptEntries.CountAsync(t => t.AgentSessionId == Id && t.Kind == TranscriptKinds.TurnEnd, Token);
            var working = await SessionMessageQueueService.IsWorkingAsync(evidence, Id, Token);
            Console.WriteLine($"C1205 recipient diagnostic: ends={ends} sqlWorking={working} pulls={_handler.Pulls} submissions={Caller.SubmittedBodies.Count}");
            foreach (var entry in _pumpLog.Entries.Concat(_runtimeLog.Entries).Concat(_queueLog.Entries))
                Console.WriteLine($"C1205 {entry.Level} {entry.Message} exception={entry.Exception?.GetType().Name}");
        }
        await ReconcileAsync(2);
        var notes = await NotesAsync();
        notes.Count.ShouldBe(Tasks.Count);
        Caller.SubmittedBodies.Count.ShouldBe(Tasks.Count);
        var rows = await RowsAsync();
        foreach (var note in notes)
        {
            var original = before.Single(n => n.TaskId == note.TaskId);
            note.Id.ShouldBe(original.Id);
            note.SourceEventId.ShouldBe(original.SourceEventId);
            note.ParentSessionId.ShouldBe(Id);
            note.ContentDigest.ShouldBe(original.ContentDigest);
            note.State.ShouldBe(LandNotificationState.Confirmed);
            note.ConfirmingPromptSequence.ShouldNotBeNull();
            var row = rows.Single(r => r.SourceTaskId == note.TaskId);
            row.Id.ShouldBe(queued.Single(r => r.SourceTaskId == note.TaskId).Id);
            note.QueueMessageId.ShouldBe(row.Id);
            row.DeliveryAttempts.ShouldBe(expectedAttempts);
            var rendering = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson).ShouldNotBeNull();
            rendering.WireSha256.ShouldBe(TaskCompletionNotification.Sha256(rendering.WireText));
            await using var db = World.CreateContext();
            var prompt = await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == Id
                && t.Sequence == note.ConfirmingPromptSequence && t.Kind == TranscriptKinds.UserPrompt, Token);
            prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value);
            prompt.Text!.ReplaceLineEndings("\n").ShouldBe(rendering.WireText.ReplaceLineEndings("\n"));
            TaskCompletionNotification.Sha256(prompt.Text.ReplaceLineEndings("\n")).ShouldBe(rendering.WireSha256);
            Caller.SubmittedBodies.Count(body => body.ReplaceLineEndings("\n") == prompt.Text.ReplaceLineEndings("\n")).ShouldBe(1);
        }
    }

    private async Task StopPumpAsync()
    {
        if (_pump is not null)
        {
            _handler.ReleaseGap.TrySetResult();
            await _pumpLoop!.DisposeAsync(); _pumpLoop = null;
            _pump = null;
        }
        if (_gapActionsLoop is not null)
        {
            await _gapActionsLoop.DisposeAsync();
            _gapActionsLoop = null;
        }
    }

    private async Task StopWorkerAsync()
    {
        if (_worker is null) return;
        await _workerLoop!.DisposeAsync(); _workerLoop = null;
        _worker = null;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        Flushes.ReleaseHold.TrySetResult();
        _workerScopes.Release.TrySetResult();
        Boundary.Release.TrySetResult();
        _handler.ReleaseGap.TrySetResult();
        await StopWorkerAsync();
        await StopPumpAsync();
        if (_adapter is not null) await _adapter.DisposeAsync();
        if (World is not null) await World.DisposeAsync();
        await _continuations.DisposeAsync();
        await Caller.DisposeAsync();
        _http.Dispose();
        _handler.Dispose();
        _stop.Dispose();
    }

    public async Task ReconcileAsync(int passes = 1)
    {
        for (var pass = 0; pass < passes; pass++)
        foreach (var note in await NotesAsync())
        {
            await using var scope = World.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(note.Id, Token);
        }
    }

    public async Task MakeIdleAsync()
    {
        _handler.Append(TranscriptKinds.TurnEnd, "", false);
        await Runtime.CatchUpTranscriptAsync(Id, Token);
        (await WorkingAsync()).ShouldBeFalse();
    }
    public void MissEnd() => _handler.Append(TranscriptKinds.TurnEnd, "", false);
    public async Task NewTurnAsync()
    {
        var entry = _handler.Append(TranscriptKinds.UserPrompt, "new authoritative work", false);
        var mapped = new SessionRunnerTranscriptEvent(entry.SessionId, entry.Sequence, entry.Kind, entry.Uuid, entry.ParentUuid,
            entry.Timestamp, entry.Role, entry.Text, entry.ToolName, entry.ToolInput, entry.ToolUseId, entry.ToolIsError, entry.StopReason);
        await Runtime.PersistTranscriptAsync(Id, [mapped]);
    }
    public async Task<bool> WorkingAsync()
    { await using var db = World.CreateContext(); return await SessionMessageQueueService.IsWorkingAsync(db, Id, Token); }
    public async Task<List<TranscriptEntry>> PromptsAsync(Guid? session = null)
    { await using var db = World.CreateContext(); return await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == (session ?? Id)
        && t.Kind == TranscriptKinds.UserPrompt).OrderBy(t => t.Sequence).ToListAsync(Token); }
    public async Task<int> EndCountAsync()
    { await using var db = World.CreateContext(); return await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == Id && t.Kind == TranscriptKinds.TurnEnd, Token); }
    public async Task ProbeAsync()
    {
        // A healthy live stream admits each subsequent turn boundary as action
        // debt. Own that loop before probing so all five notes can progress.
        if (_pump is not null) await StartActionsAsync();
        await Recovery.TickAsync(Token);
        await Recovery.JoinCurrentProbesAsync().WaitAsync(Token);
    }
    public async Task StartStreamAsync()
    {
        _handler.Drained = Signal();
        _pump = ActivatorUtilities.CreateInstance<SessionRunnerEventPump>(World.Services);
        _pumpLoop = new OwnedLoop(_pump, Token);
        await _pumpLoop.StartAsync(_continuations);
        await JoinDeliveryAsync(_handler.Drained.Task);
    }
    public async Task KeepaliveAsync()
    {
        var observed = _handler.Stream!.ObserveNextDrain();
        _handler.Pulse();
        await observed.WaitAsync(Token);
    }
    public async Task DrainWakeupsAsync()
    {
        while (Flushes.PendingCount != 0)
        {
            await using var reader = Flushes.ReadAllAsync(Token).GetAsyncEnumerator();
            (await reader.MoveNextAsync()).ShouldBeTrue(); Flushes.Complete(reader.Current);
        }
        Flushes.ClearAudit();
    }
    public Task DeliverExplicitlyAsync() => JoinDeliveryAsync(Queue.FlushIfIdleAsync(Id, Token));
    public async Task ScanNotificationsAsync()
    {
        var observed = Boundary.Observe("notification-scan");
        var worker = ActivatorUtilities.CreateInstance<AgentTaskLandNotificationHostedService>(World.Services);
        await worker.StartAsync(Token);
        try { await observed.WaitAsync(Token); }
        finally { await worker.StopAsync(CancellationToken.None); worker.Dispose(); }
    }
    public async Task ReplaySettlementAsync()
    {
        var task = await World.TaskAsync(Tasks.Single());
        await World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(task.AgentSessionId!.Value, Token);
    }
    public async Task WaitForSubmittedPromptAsync()
    {
        var body = Caller.SubmittedBodies.Last();
        while (!(await PromptsAsync()).Any(p => p.Text?.ReplaceLineEndings("\n") == body.ReplaceLineEndings("\n")))
        { Token.ThrowIfCancellationRequested(); await Task.Yield(); }
    }
    public async Task<Guid> AddSiblingAsync()
    {
        var sibling = Guid.NewGuid();
        await using var db = World.CreateContext();
        db.AgentSessions.Add(C544World.Session(sibling, "c1205-other-recipient", _handler.Generation));
        await db.SaveChangesAsync(Token);
        for (var i = 0; i < 4; i++) _handler.Append(TranscriptKinds.UserPrompt, $"prior sibling input {i}", false, sibling);
        await Runtime.CatchUpTranscriptAsync(sibling, Token);
        return sibling;
    }
    public Task PullAsync(Guid session) => Runtime.CatchUpTranscriptAsync(session, Token);
    public async Task SaturateWorkerAsync()
    {
        _workerScopes.Arm();
        for (var i = 0; i < 128; i++) Flushes.TryEnqueue(Guid.NewGuid()).ShouldBeTrue();
        await _workerScopes.AllEntered.Task.WaitAsync(Token);
        for (var i = 0; i < 4; i++) Flushes.TryEnqueue(Guid.NewGuid()).ShouldBeTrue();
        Flushes.ClearAudit();
    }
    public void ReleaseWorkerCapacity() => _workerScopes.Release.TrySetResult();
    public void SaturateWithoutWorker()
    { for (var i = 0; i < 128; i++) Flushes.TryEnqueue(Guid.NewGuid()).ShouldBeTrue(); Flushes.ClearAudit(); }
    public Task StopCompletionWorkerAsync() => StopWorkerAsync();
    public async Task AdvanceRetryAsync()
    {
        var latest = (await NotesAsync()).Select(n => n.NextAttemptAt).DefaultIfEmpty(Clock.GetUtcNow().UtcDateTime).Max();
        if (latest > Clock.GetUtcNow().UtcDateTime) Clock.AdvanceTo(new DateTimeOffset(latest, TimeSpan.Zero));
    }
    public int TimerSequence => Clock.Events.LastOrDefault()?.Sequence ?? 0;
    public async Task<ControlledTimeProvider.TimerEvent> NextBackstopAsync(int after, CancellationToken ct)
    {
        while (true)
        {
            var timer = await Clock.WaitForTimerAsync(e => e.Action == "create" && e.DueTime >= TimeSpan.FromMinutes(14), after, ct);
            after = timer.Sequence;
            // WaitAsync may allocate a timer for an already-ready signal and
            // immediately cancel it. Only a live, debt-free wait is a pass end.
            var recovery = Flushes.Recovery;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var gate = recovery.GetType().GetField("_gate", flags)!.GetValue(recovery)!;
            lock (gate)
            {
                var noHints = !(bool)recovery.GetType().GetField("_sweep", flags)!.GetValue(recovery)!
                    && ((HashSet<Guid>)recovery.GetType().GetField("_tasks", flags)!.GetValue(recovery)!).Count == 0
                    && ((Dictionary<Guid, DateTime>)recovery.GetType().GetField("_sessions", flags)!.GetValue(recovery)!).Count == 0;
                if (noHints && Clock.TimerStates[timer.TimerId] != "dispose") return timer;
            }
        }
    }

    public sealed class RecordingFlushes : CompletionNoteFlushQueue
    {
        public ConcurrentQueue<Guid> Accepted { get; } = new();
        public ConcurrentQueue<Guid> Rejected { get; } = new();
        public bool Hold;
        public bool DropHeld;
        public TaskCompletionSource Held { get; } = Signal();
        public TaskCompletionSource ReleaseHold { get; } = Signal();
        public int PendingCount => ((System.Collections.IDictionary)typeof(CompletionNoteFlushQueue)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!).Count;
        public void ClearAudit() { Accepted.Clear(); Rejected.Clear(); }
        public override bool TryEnqueue(Guid id)
        {
            if (Hold) { Held.TrySetResult(); ReleaseHold.Task.GetAwaiter().GetResult(); if (DropHeld) return false; }
            var accepted = base.TryEnqueue(id);
            (accepted ? Accepted : Rejected).Enqueue(id);
            return accepted;
        }
    }

    public sealed class DeliveryBoundary : LandDeliveryBoundary
    {
        private readonly ConcurrentQueue<string> _reached = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _observations = new();
        public string? ThrowOnce;
        public string? HoldAt;
        public int Throws;
        public TaskCompletionSource Held { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public int Count(string boundary) => _reached.Count(b => b == boundary);
        public Task Observe(string boundary)
        { var signal = Signal(); _observations[boundary] = signal; return signal.Task; }
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            _reached.Enqueue(boundary);
            if (_observations.TryGetValue(boundary, out var observation)) observation.TrySetResult();
            if (HoldAt == boundary) { Held.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (ThrowOnce == boundary) { ThrowOnce = null; Interlocked.Increment(ref Throws); throw new IOException("c1205 boundary cut: " + boundary); }
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false) { Timeout = Timeout.InfiniteTimeSpan }; }

    private sealed class RunnerHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RunnerTranscriptEvent> _entries = [];
        private readonly SessionRunnerEventHub _hub = new();
        private bool _gap;
        public Guid Id;
        public DateTime Generation;
        public int Pulls;
        public bool WrongGeneration, FailPull;
        public HubStream? Stream;
        public TaskCompletionSource GapCaptured = Signal(), ReleaseGap = Signal(), Drained = Signal();
        public void ArmGap() { _gap = true; GapCaptured = Signal(); ReleaseGap = Signal(); Drained = Signal(); }
        public RunnerTranscriptEvent Append(string kind, string text, bool publish, Guid? sessionId = null)
        {
            RunnerTranscriptEvent entry;
            lock (_gate)
            {
                var seq = _entries.Count + 1;
                entry = new(sessionId ?? Id, seq, kind, Guid.NewGuid().ToString(), null,
                    new DateTimeOffset(Generation, TimeSpan.Zero).AddMilliseconds(seq),
                    kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text, null, null, null, null,
                    kind == TranscriptKinds.TurnEnd ? TranscriptKinds.StopReasons.EndTurn : null);
                _entries.Add(entry);
            }
            if (publish) _hub.Publish(SessionRunnerEventNames.SessionTranscript, entry);
            return entry;
        }
        public void Pulse() => _hub.Publish("c1205-keepalive", "controlled keepalive");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var session = new RunnerSessionDto(Id, 4242, Generation, "Running", null, "Unknown", 0);
            if (path == "/sessions") return Json(new[] { session });
            if (path == $"/sessions/{Id:D}") return Json(session);
            if (path.StartsWith("/sessions/") && path.EndsWith("/transcript"))
            {
                var requested = Guid.Parse(path.Split('/')[2]);
                Interlocked.Increment(ref Pulls);
                if (FailPull) throw new HttpRequestException("planned authoritative pull failure");
                RunnerTranscriptDto snapshot;
                lock (_gate)
                {
                    var rows = _entries.Where(e => e.SessionId == requested).ToArray();
                    snapshot = new(requested, rows, rows.LastOrDefault()?.Sequence ?? 0,
                        AcceptedStartedAt: WrongGeneration ? Generation.AddSeconds(1) : Generation);
                }
                if (_gap)
                {
                    _gap = false;
                    GapCaptured.TrySetResult();
                    await ReleaseGap.Task.WaitAsync(ct);
                }
                return Json(snapshot);
            }
            if (path == "/events")
            {
                var lifetime = new CancellationTokenSource();
                Stream = new HubStream(_hub.Subscribe(lifetime.Token), Drained, lifetime);
                return new(HttpStatusCode.OK) { Content = new StreamContent(Stream) };
            }
            return new(HttpStatusCode.NotFound) { Content = new StringContent(path) };
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HubStream(ChannelReader<RunnerServerSentEvent> reader, TaskCompletionSource drained,
        CancellationTokenSource lifetime) : Stream
    {
        private byte[]? _bytes = Encoding.UTF8.GetBytes(": keepalive\n\n");
        private int _offset;
        private int _disposed;
        private int _readingEvent;
        private int _waiting;
        private TaskCompletionSource _drained = drained;
        private int _keepalives = 1;
        public int Keepalives => Volatile.Read(ref _keepalives);
        public Task ObserveNextDrain()
        { var observed = Signal(); Volatile.Write(ref _drained, observed); return observed.Task; }
        public bool Quiescent => Volatile.Read(ref _waiting) != 0 && Volatile.Read(ref _readingEvent) == 0
            && (!reader.CanCount || reader.Count == 0);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_bytes is null)
            {
                Volatile.Write(ref _readingEvent, 0);
                if (!reader.TryRead(out var frame))
                {
                    Volatile.Write(ref _waiting, 1);
                    Volatile.Read(ref _drained).TrySetResult();
                    frame = await reader.ReadAsync(ct);
                }
                Volatile.Write(ref _waiting, 0);
                Volatile.Write(ref _readingEvent, 1);
                if (frame.EventName == "c1205-keepalive")
                { Interlocked.Increment(ref _keepalives); _bytes = Encoding.UTF8.GetBytes(": keepalive\n\n"); }
                else _bytes = Encoding.UTF8.GetBytes($"event: {frame.EventName}\ndata: {frame.Json}\n\n");
                _offset = 0;
            }
            var count = Math.Min(buffer.Length, _bytes.Length - _offset);
            _bytes.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            if (_offset == _bytes.Length) _bytes = null;
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lifetime.Cancel();
                lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class WorkerScopeGate
    {
        private int _entered;
        private bool _armed;
        public TaskCompletionSource AllEntered = Signal(), Release = Signal();
        public void Arm() { _entered = 0; _armed = true; AllEntered = Signal(); Release = Signal(); }
        public void Enter()
        {
            if (!_armed) return;
            if (Interlocked.Increment(ref _entered) == 4) AllEntered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class ContinuationQueue : SynchronizationContext, IAsyncDisposable
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> _queue = Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
        private readonly object _gate = new();
        private TaskCompletionSource _changed = Signal();
        private long _sequence;
        private readonly Task _runner;
        public ContinuationQueue() => _runner = Task.Run(RunAsync);
        public override void Post(SendOrPostCallback callback, object? state)
        { if (!_queue.Writer.TryWrite((callback, state))) throw new ObjectDisposedException(nameof(ContinuationQueue)); }
        private async Task RunAsync()
        {
            await foreach (var item in _queue.Reader.ReadAllAsync())
            {
                SetSynchronizationContext(this);
                try { item.Callback(item.State); }
                finally
                {
                    SetSynchronizationContext(null);
                    lock (_gate) { _sequence++; _changed.TrySetResult(); _changed = Signal(); }
                }
            }
        }
        public Task InvokeAsync(Func<Task> action)
        {
            var done = Signal();
            Post(_ => { _ = CompleteAsync(action, done); }, null);
            return done.Task;
        }
        private static async Task CompleteAsync(Func<Task> action, TaskCompletionSource done)
        { try { await action(); done.TrySetResult(); } catch (Exception ex) { done.TrySetException(ex); } }
        public Task<(bool Quiet, long Sequence)> ObserveAsync(Func<bool> read)
        {
            var done = new TaskCompletionSource<(bool, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => { try { lock (_gate) done.TrySetResult((read(), _sequence + 1)); } catch (Exception ex) { done.TrySetException(ex); } }, null);
            return done.Task;
        }
        public async Task WaitForProgressAsync(long after, CancellationToken ct)
        {
            while (true)
            {
                Task changed;
                lock (_gate) { if (_sequence > after) return; changed = _changed.Task; }
                await changed.WaitAsync(ct);
            }
        }
        public async ValueTask DisposeAsync() { _queue.Writer.TryComplete(); await _runner; }
    }

    // T37..T44 drive the actual ExecuteAsync loops under owned scheduling. This
    // proves service reconstruction, not the framework's launcher scheduling.
    private sealed class OwnedLoop(BackgroundService service, CancellationToken fixtureToken) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(fixtureToken);
        private Task? _run;
        public async Task StartAsync(ContinuationQueue? continuations)
        {
            if (continuations is null) { await service.StartAsync(_stop.Token); return; }
            await continuations.InvokeAsync(() =>
            {
                _run = (Task)service.GetType().GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(service, [_stop.Token])!;
                return Task.CompletedTask;
            });
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try
            {
                if (_run is null) await service.StopAsync(CancellationToken.None);
                else try { await _run; } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            }
            finally { service.Dispose(); _stop.Dispose(); }
        }
    }
    private sealed class ObservedScopes(IServiceScopeFactory inner, WorkerScopeGate gate, Action completed) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(inner.CreateScope(), gate, completed);
        private sealed class Scope(IServiceScope inner, WorkerScopeGate gate, Action completed) : IServiceScope, IAsyncDisposable
        {
            private readonly Provider _provider = new(inner.ServiceProvider, gate);
            public IServiceProvider ServiceProvider => _provider;
            public void Dispose() { inner.Dispose(); RecordCycle(); }
            public async ValueTask DisposeAsync()
            {
                if (inner is IAsyncDisposable owned) await owned.DisposeAsync(); else inner.Dispose();
                RecordCycle();
            }
            private void RecordCycle() { if (_provider.DeliveryCycle) completed(); }
        }
        private sealed class Provider(IServiceProvider inner, WorkerScopeGate gate) : IServiceProvider
        {
            private bool _resolved;
            public bool DeliveryCycle { get; private set; }
            public object? GetService(Type type)
            {
                // FlushAsync resolves the queue first; recovery scopes resolve
                // AppDbContext first. Count completed delivery scopes, not scans.
                if (!_resolved) { DeliveryCycle = type == typeof(SessionMessageQueueService); _resolved = true; }
                if (type == typeof(SessionMessageQueueService)) gate.Enter();
                return inner.GetService(type);
            }
        }
    }

    // The provider/tailer and composer survive; only the old server's output
    // subscription is disposed at the graph cut in T37.
    private sealed class EpochAdapter : IAgentProtocolAdapter
    {
        private readonly FakeAgentProtocolAdapter _inner;
        public EpochAdapter(FakeAgentProtocolAdapter inner) { _inner = inner; inner.OnTextDelta += Forward; }
        private void Forward(string text) => OnTextDelta?.Invoke(text);
        public event Action<string>? OnTextDelta;
        public Task<int> Exited => _inner.Exited;
        public int? Pid => _inner.Pid;
        public AgentExitReason ExitReason => _inner.ExitReason;
        public string? AuditDirectory => _inner.AuditDirectory;
        public Task StartAsync(AgentLaunchSpec spec, CancellationToken ct) => _inner.StartAsync(spec, ct);
        public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) => _inner.KillAsync(timeout, ct);
        public Task SendPromptAsync(string prompt, CancellationToken ct) => _inner.SendPromptAsync(prompt, ct);
        public Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct) => _inner.WaitForFirstPromptOutputAsync(timeout, ct);
        public Task SendInputAsync(string input, CancellationToken ct) => _inner.SendInputAsync(input, ct);
        public Task<ProviderDialogObservation?> ObserveDialogAsync(Guid sessionId, CancellationToken ct) =>
            _inner.ObserveDialogAsync(sessionId, ct);
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(RunnerConditionalInputRequest request, CancellationToken ct) =>
            _inner.SendConditionalInputAsync(request, ct);
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => _inner.ResizeAsync(cols, rows, ct);
        public Task<bool> WaitForReadyAsync(CancellationToken ct) => _inner.WaitForReadyAsync(ct);
        public Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct) => _inner.WaitForTurnCompleteAsync(ct);
        public string SnapshotRawOutput() => _inner.SnapshotRawOutput();
        public Task<string> SnapshotRawOutputAsync(CancellationToken ct) => _inner.SnapshotRawOutputAsync(ct);
        public string SnapshotRenderedScreen() => _inner.SnapshotRenderedScreen();
        public ValueTask DisposeAsync() { _inner.OnTextDelta -= Forward; return ValueTask.CompletedTask; }
    }
}

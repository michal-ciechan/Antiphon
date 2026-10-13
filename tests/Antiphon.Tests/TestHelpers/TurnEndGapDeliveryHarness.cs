using System.Net;
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
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly RunnerHandler _handler = new();
    private readonly HttpClient _http;
    private readonly bool _cache;
    private SessionRunnerEventPump? _pump;
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(90));
    public List<Guid> Tasks { get; } = [];
    public Guid Id => World.CallerSessionId;
    public AgentSessionRuntime Runtime => World.Services.GetRequiredService<AgentSessionRuntime>();
    public SessionMessageQueueService Queue => World.Services.GetRequiredService<SessionMessageQueueService>();
    public Task GapCaptured => _handler.GapCaptured.Task;
    public Task PumpDrained => _handler.Drained.Task;
    public CancellationToken Token => _stop.Token;

    private TurnEndGapDeliveryHarness(bool cache)
    {
        _cache = cache;
        _http = new HttpClient(_handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static async Task<TurnEndGapDeliveryHarness> CreateAsync(bool cache)
    {
        var h = new TurnEndGapDeliveryHarness(cache);
        try
        {
            h.World = await C544World.CreateAsync(configure: h.Configure);
            await using var db = h.World.CreateContext();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.Id);
            h._handler.Id = h.Id;
            h._handler.Generation = session.StartedAt;
            h.Attach();
            h._handler.Append(TranscriptKinds.UserPrompt, "dispatch five tasks", publish: false);
            await h.Runtime.CatchUpTranscriptAsync(h.Id, h.Token);
            return h;
        }
        catch { await h.DisposeAsync(); throw; }
    }

    private void Configure(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(new SessionStateSettings { Enabled = _cache }));
        services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
        services.AddSingleton<SessionStateStore>();
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
    }

    private void Attach()
    {
        Runtime.Register(Id, Caller);
        Caller.OnSubmitted = body =>
        {
            _handler.Append(TranscriptKinds.UserPrompt, body.ReplaceLineEndings("\n"), publish: true);
            _handler.Append(TranscriptKinds.TurnEnd, "", publish: true);
            return Task.CompletedTask;
        };
    }

    public async Task ProduceFiveAsync()
    {
        for (var i = 0; i < 5; i++)
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
        rows.Count.ShouldBe(5);
        rows.ShouldAllBe(r => r.Status == QueuedMessageStatus.Pending && r.DeliveryAttempts == 0);
        Caller.SubmittedBodies.ShouldBeEmpty();
        await using var db = World.CreateContext();
        (await SessionMessageQueueService.IsWorkingAsync(db, Id, Token)).ShouldBeTrue();
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
        await StopPumpAsync();
        await World.RestartAsync();
        Attach();
    }

    public async Task StartGapAsync()
    {
        _handler.ArmGap();
        _pump = ActivatorUtilities.CreateInstance<SessionRunnerEventPump>(World.Services);
        await _pump.StartAsync(Token);
        await GapCaptured.WaitAsync(Token);
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
                    || e.DueTime == TimeSpan.FromMilliseconds(500))
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

    public async Task AssertReceiptsAsync(List<AgentTaskLandNotification> before, List<SessionQueuedMessage> queued)
    {
        for (var scan = 0; scan < 2; scan++)
        {
            foreach (var note in before)
            {
                await using var scope = World.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(note.Id, Token);
            }
        }
        var notes = await NotesAsync();
        notes.Count.ShouldBe(5);
        Caller.SubmittedBodies.Count.ShouldBe(5);
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
            row.DeliveryAttempts.ShouldBe(1);
            var rendering = TaskCompletionNotification.TryReadDelivery(note.CompletionDeliveryJson).ShouldNotBeNull();
            rendering.WireSha256.ShouldBe(TaskCompletionNotification.Sha256(rendering.WireText));
            await using var db = World.CreateContext();
            var prompt = await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == Id
                && t.Sequence == note.ConfirmingPromptSequence && t.Kind == TranscriptKinds.UserPrompt, Token);
            prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value);
            prompt.Text!.ReplaceLineEndings("\n").ShouldBe(rendering.WireText.ReplaceLineEndings("\n"));
            Caller.SubmittedBodies.Count(body => body.ReplaceLineEndings("\n") == prompt.Text.ReplaceLineEndings("\n")).ShouldBe(1);
        }
    }

    private async Task StopPumpAsync()
    {
        if (_pump is null) return;
        _handler.ReleaseGap.TrySetResult();
        await _pump.StopAsync(CancellationToken.None);
        _pump.Dispose();
        _pump = null;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _handler.ReleaseGap.TrySetResult();
        await StopPumpAsync();
        if (World is not null) await World.DisposeAsync();
        await Caller.DisposeAsync();
        _http.Dispose();
        _handler.Dispose();
        _stop.Dispose();
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
        public TaskCompletionSource GapCaptured = Signal(), ReleaseGap = Signal(), Drained = Signal();
        public void ArmGap() { _gap = true; GapCaptured = Signal(); ReleaseGap = Signal(); Drained = Signal(); }
        public void Append(string kind, string text, bool publish)
        {
            RunnerTranscriptEvent entry;
            lock (_gate)
            {
                var seq = _entries.Count + 1;
                entry = new(Id, seq, kind, Guid.NewGuid().ToString(), null,
                    new DateTimeOffset(Generation, TimeSpan.Zero).AddTicks(seq), "user", text, null, null, null, null, null);
                _entries.Add(entry);
            }
            if (publish) _hub.Publish(SessionRunnerEventNames.SessionTranscript, entry);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var session = new RunnerSessionDto(Id, 4242, Generation, "Running", null, "Unknown", 0);
            if (path == "/sessions") return Json(new[] { session });
            if (path == $"/sessions/{Id:D}") return Json(session);
            if (path == $"/sessions/{Id:D}/transcript")
            {
                RunnerTranscriptDto snapshot;
                lock (_gate) snapshot = new(Id, _entries.ToArray(), _entries.Count, AcceptedStartedAt: Generation);
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
                var stream = new HubStream(_hub.Subscribe(ct), Drained);
                return new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            }
            return new(HttpStatusCode.NotFound) { Content = new StringContent(path) };
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HubStream(ChannelReader<RunnerServerSentEvent> reader, TaskCompletionSource drained) : Stream
    {
        private byte[]? _bytes = Encoding.UTF8.GetBytes(": keepalive\n\n");
        private int _offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_bytes is null)
            {
                drained.TrySetResult();
                var frame = await reader.ReadAsync(ct);
                _bytes = Encoding.UTF8.GetBytes($"event: {frame.EventName}\ndata: {frame.Json}\n\n");
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
    }
}

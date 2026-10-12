using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1205 H1/H2. Real <see cref="SessionRunnerHttpClient"/> and
/// <see cref="SessionRunnerEventPump"/> over <see cref="SessionStateTestFixture"/>'s
/// isolated database and commit gate, with a frame-gated HTTP stream and the real
/// <see cref="SessionRunnerEventHub"/>. One SSE frame per read; the read after a
/// consumed keepalive blocks until released. No sleeps.
/// </summary>
internal sealed class TurnEndGapHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly SessionStateTestFixture _fixture;
    private readonly GapHandler _handler;
    private readonly HttpClient _http;
    private readonly SessionRunnerEventPump _pump;
    private readonly AgentSessionRuntime _runtime;
    private readonly TaskCompletionSource _otherEntered = NewSignal();
    private readonly TaskCompletionSource _releaseOther = NewSignal();
    private readonly TaskCompletionSource _connected = NewSignal();
    private readonly TaskCompletionSource _releaseSubscribe = NewSignal();
    private readonly TaskCompletionSource _postKeepalive = NewSignal();
    private readonly TaskCompletionSource _releaseObservation = NewSignal();
    private readonly List<string> _order = [];
    private readonly object _orderGate = new();
    private CancellationTokenSource? _stop;
    private int _subscribeCount;

    public Guid SessionId => _fixture.SessionId;
    public string PriorEndUuid { get; } = "c1205-prior-end";
    public string ToolCallUuid { get; } = "c1205-tool-call";
    public string MissingEndUuid { get; } = "c1205-missing-end";
    public int CallerTranscriptGets => _handler.CallerTranscriptGets;
    public int SubscribeCount => Volatile.Read(ref _subscribeCount);

    public IReadOnlyList<string> Order
    {
        get
        {
            lock (_orderGate)
                return _order.ToArray();
        }
    }

    private TurnEndGapHarness(SessionStateTestFixture fixture)
    {
        _fixture = fixture;
        var settings = new Antiphon.Server.Application.Settings.SessionRunnerSettings
        {
            BaseUrl = "http://runner.invalid",
            Enabled = true,
            EventReconnectDelayMs = 60_000,
            EventStreamIdleTimeoutSeconds = 600,
            ListTimeoutSeconds = 30,
            RequestTimeoutSeconds = 60,
        };
        var hub = new SessionRunnerEventHub();
        var tail = Tail(fixture.SessionId);
        _handler = new GapHandler(this, hub, fixture.SessionId, fixture.OtherId, tail);
        _http = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new SessionRunnerHttpClient(
            _http,
            new SharedHandlerFactory(_handler),
            Options.Create(settings),
            time: fixture.Services.GetRequiredService<TimeProvider>());
        var loggerFactory = fixture.Services.GetRequiredService<ILoggerFactory>();
        _runtime = new AgentSessionRuntime(
            client,
            fixture.Services.GetRequiredService<IEventBus>(),
            fixture.Services.GetRequiredService<IOptions<AgentSessionSettings>>(),
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Services.GetRequiredService<TimeProvider>(),
            loggerFactory.CreateLogger<AgentSessionRuntime>(),
            states: fixture.Store);
        var scopes = new OverlayScopeFactory(fixture.Services, client, _runtime);
        _pump = new SessionRunnerEventPump(
            scopes,
            Options.Create(settings),
            loggerFactory.CreateLogger<SessionRunnerEventPump>());
    }

    public static async Task<TurnEndGapHarness> CreateAsync()
    {
        var fixture = await SessionStateTestFixture.CreateAsync();
        try
        {
            return new TurnEndGapHarness(fixture);
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task ReachUnsubscribedTailAsync(CancellationToken ct)
    {
        await HoldCatchUpAsync(ct);
        _handler.AppendTailAndPublish();
    }

    public async Task ReachSubscribedKeepaliveAsync(CancellationToken ct)
    {
        await HoldCatchUpAsync(ct);
        _handler.AppendTailAndPublish();
        _releaseOther.TrySetResult();
        await _connected.Task.WaitAsync(ct);
        _releaseSubscribe.TrySetResult();
        await _postKeepalive.Task.WaitAsync(ct);
    }

    public Task<bool> GracePullAsync(CancellationToken ct) =>
        _runtime.CatchUpTranscriptAsync(SessionId, ct);

    public async Task<int> CountAsync(string uuid, string kind)
    {
        await using var db = _fixture.Db();
        return await db.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == SessionId && t.Uuid == uuid && t.Kind == kind);
    }

    public async Task<bool> SqlWorkingAsync(CancellationToken ct)
    {
        await using var db = _fixture.Db();
        return await SessionMessageQueueService.IsWorkingAsync(db, SessionId, ct);
    }

    public async Task<bool> CacheWorkingAsync(CancellationToken ct) =>
        (await _fixture.Store.ReadAsync(SessionId, ct)).Working;

    public async ValueTask DisposeAsync()
    {
        _releaseOther.TrySetResult();
        _releaseSubscribe.TrySetResult();
        _releaseObservation.TrySetResult();
        try
        {
            _stop?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _pump.StopAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }

        _http.Dispose();
        if (_handler.Producer is { } producer)
        {
            try
            {
                await producer.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }
        }

        _handler.Dispose();
        await _fixture.DisposeAsync();
        _stop?.Dispose();
    }

    private async Task HoldCatchUpAsync(CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _pump.StartAsync(_stop.Token);
        await _otherEntered.Task.WaitAsync(ct);
    }

    private void Note(string step)
    {
        lock (_orderGate)
            _order.Add(step);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static List<RunnerTranscriptEvent> Tail(Guid sessionId)
    {
        var origin = new DateTimeOffset(2026, 10, 12, 11, 19, 16, TimeSpan.Zero);
        return
        [
            Entry(sessionId, 3, TranscriptKinds.ToolResult, "c1205-tool-result", origin.AddSeconds(2), "tool result", "Read"),
            Entry(sessionId, 4, TranscriptKinds.AssistantText, "c1205-assistant-text", origin.AddSeconds(3), "answer", null),
            Entry(sessionId, 5, TranscriptKinds.TurnEnd, "c1205-missing-end", origin.AddSeconds(4), "end missing", null),
        ];
    }

    private static RunnerTranscriptEvent Entry(
        Guid sessionId, long sequence, string kind, string uuid, DateTimeOffset timestamp, string text, string? toolName) =>
        new(sessionId, sequence, kind, uuid, null, timestamp, "assistant", text, toolName, null, null, null, null);

    private sealed class SharedHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private sealed class OverlayScopeFactory(
        IServiceProvider inner, ISessionRunnerClient client, AgentSessionRuntime runtime) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new OverlayScope(inner.CreateScope(), client, runtime);

        private sealed class OverlayScope(IServiceScope inner, ISessionRunnerClient client, AgentSessionRuntime runtime)
            : IServiceScope, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => new OverlayProvider(inner.ServiceProvider, client, runtime);

            public void Dispose() => inner.Dispose();

            public async ValueTask DisposeAsync()
            {
                if (inner is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else
                    inner.Dispose();
            }
        }

        private sealed class OverlayProvider(
            IServiceProvider inner, ISessionRunnerClient client, AgentSessionRuntime runtime) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(ISessionRunnerClient))
                    return client;
                if (serviceType == typeof(AgentSessionRuntime))
                    return runtime;
                return inner.GetService(serviceType);
            }
        }
    }

    private sealed class GapHandler : HttpMessageHandler
    {
        private readonly TurnEndGapHarness _harness;
        private readonly SessionRunnerEventHub _hub;
        private readonly Guid _sessionId;
        private readonly Guid _otherId;
        private readonly List<RunnerTranscriptEvent> _tail;
        private readonly List<RunnerTranscriptEvent> _snapshot;
        private readonly object _snapshotGate = new();
        private readonly FrameGateStream _frames;
        private int _callerGets;
        private int _otherGets;
        private int _events;
        private Task? _producer;

        public GapHandler(TurnEndGapHarness harness, SessionRunnerEventHub hub, Guid sessionId, Guid otherId, List<RunnerTranscriptEvent> tail)
        {
            _harness = harness;
            _hub = hub;
            _sessionId = sessionId;
            _otherId = otherId;
            _tail = tail;
            var origin = new DateTimeOffset(2026, 10, 12, 11, 19, 16, TimeSpan.Zero);
            _snapshot =
            [
                Entry(sessionId, 1, TranscriptKinds.TurnEnd, harness.PriorEndUuid, origin, "end prior", null),
                Entry(sessionId, 2, TranscriptKinds.ToolCall, harness.ToolCallUuid, origin.AddSeconds(1), "tool call", "Read"),
            ];
            _frames = new FrameGateStream(harness._postKeepalive, harness._releaseObservation, harness.Note);
        }

        public int CallerTranscriptGets => Volatile.Read(ref _callerGets);

        public Task? Producer => _producer;

        public void AppendTailAndPublish()
        {
            lock (_snapshotGate)
                _snapshot.AddRange(_tail);
            _harness.Note("custody-append");
            foreach (var entry in _tail)
                _hub.Publish(SessionRunnerEventNames.SessionTranscript, entry);
            _harness.Note($"hub-publish subscribers={Volatile.Read(ref _harness._subscribeCount)}");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/sessions")
                return Json(new[] { Session(_sessionId), Session(_otherId) });
            if (path == $"/sessions/{_sessionId:D}/transcript")
            {
                Interlocked.Increment(ref _callerGets);
                _harness.Note("caller-transcript");
                return Json(Snapshot(_sessionId));
            }

            if (path == $"/sessions/{_otherId:D}/transcript")
            {
                if (Interlocked.Increment(ref _otherGets) == 1)
                {
                    _harness.Note("other-pull-entered");
                    _harness._otherEntered.TrySetResult();
                    await _harness._releaseOther.Task.WaitAsync(cancellationToken);
                    _harness.Note("other-pull-released");
                }

                return Json(Snapshot(_otherId));
            }

            if (path == "/events")
            {
                if (Interlocked.Increment(ref _events) == 1)
                    _producer = Task.Run(() => ProduceAsync(cancellationToken), CancellationToken.None);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(_frames),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(path),
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _frames.Dispose();
            base.Dispose(disposing);
        }

        private async Task ProduceAsync(CancellationToken ct)
        {
            try
            {
                await _frames.EnqueueAsync(": connected\n\n", armAfter: false, ct);
                _harness._connected.TrySetResult();
                await _harness._releaseSubscribe.Task.WaitAsync(ct);
                var reader = _hub.Subscribe(ct);
                var replayed = reader.TryRead(out _);
                Interlocked.Increment(ref _harness._subscribeCount);
                _harness.Note(replayed ? "hub-subscribed replay=true" : "hub-subscribed replay=false");
                await _frames.EnqueueAsync(": keepalive\n\n", armAfter: true, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _harness._connected.TrySetException(ex);
                _harness._postKeepalive.TrySetException(ex);
            }
        }

        private RunnerTranscriptDto Snapshot(Guid sessionId)
        {
            List<RunnerTranscriptEvent> entries;
            lock (_snapshotGate)
                entries = sessionId == _sessionId ? _snapshot.ToList() : [];
            var last = entries.Count == 0 ? 0 : entries[^1].Sequence;
            return new RunnerTranscriptDto(sessionId, entries, last, false, null);
        }

        private static RunnerSessionDto Session(Guid id) =>
            new(id, 4242, new DateTime(2026, 10, 12, 11, 19, 0, DateTimeKind.Utc), "Running", null, "Unknown", 0);

        private static HttpResponseMessage Json(object body)
        {
            var json = JsonSerializer.Serialize(body, WebJson);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FrameGateStream : Stream
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        private readonly TaskCompletionSource _postKeepalive;
        private readonly TaskCompletionSource _releaseObservation;
        private readonly Action<string> _note;
        private byte[]? _current;
        private TaskCompletionSource? _currentDone;
        private bool _currentArm;
        private int _offset;
        private bool _barrierPending;

        public FrameGateStream(TaskCompletionSource postKeepalive, TaskCompletionSource releaseObservation, Action<string> note)
        {
            _postKeepalive = postKeepalive;
            _releaseObservation = releaseObservation;
            _note = note;
        }

        public async Task EnqueueAsync(string text, bool armAfter, CancellationToken ct)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _frames.Writer.WriteAsync(new Frame(Encoding.UTF8.GetBytes(text), armAfter, done), ct);
            await done.Task.WaitAsync(ct);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_barrierPending)
            {
                _barrierPending = false;
                _note("post-keepalive-read");
                _postKeepalive.TrySetResult();
                await _releaseObservation.Task.WaitAsync(cancellationToken);
            }

            if (buffer.Length == 0)
                return 0;
            if (_current is null)
            {
                Frame frame;
                try
                {
                    frame = await _frames.Reader.ReadAsync(cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    return 0;
                }

                _current = frame.Bytes;
                _currentArm = frame.ArmAfter;
                _currentDone = frame.Consumed;
                _offset = 0;
            }

            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            if (_offset >= _current.Length)
            {
                var done = _currentDone;
                var arm = _currentArm;
                _current = null;
                _currentDone = null;
                if (arm)
                {
                    _note("keepalive-consumed");
                    _barrierPending = true;
                }
                else
                {
                    _note("connected-consumed");
                }

                done?.TrySetResult();
            }

            return count;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _frames.Writer.TryComplete();
            _releaseObservation.TrySetResult();
            base.Dispose(disposing);
        }

        private sealed record Frame(byte[] Bytes, bool ArmAfter, TaskCompletionSource Consumed);
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1205 H2. One SSE frame per read. The read after an armed frame signals and blocks.
/// Iterator cases consume <see cref="SessionRunnerHttpClient"/> directly. Pump cases use the
/// real event pump over an isolated store. The runner /events route is not invoked.
/// </summary>
internal sealed class TurnEndGapReadiness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly SessionStateTestFixture? _fixture;
    private readonly GapHandler _handler;
    private readonly HttpClient _http;
    private readonly SessionRunnerHttpClient _client;
    private readonly CountingClient? _counting;
    private readonly SessionRunnerEventPump? _pump;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _order = [];
    private readonly object _orderGate = new();

    public SecondTranscriptSaveGate? Gate { get; }
    public SignalingBus? Bus { get; }
    public Guid SessionId { get; }
    public string MissingEndUuid { get; } = "c1205-gap-end";
    public Func<Connection, CancellationToken, Task>? OnConnection { get; set; }
    public int CallerGets => _handler.CallerGets;
    public int MarkerCount => _counting?.MarkerCount ?? 0;
    public int SubscribeCount => _handler.SubscribeCount;
    public bool HeadersRecorded => _handler.HeadersRecorded;
    public int HeaderStatus => _handler.HeaderStatus;
    public string? HeaderContentType => _handler.HeaderContentType;
    public bool StreamDisposed => _handler.StreamDisposed;
    public bool ResponseDisposed => _handler.ResponseDisposed;
    public bool ContentDisposed => _handler.ContentDisposed;
    public bool SubscriberClosed => _handler.SubscriberClosed;
    public Task? Producer => _handler.Producer;
    public Task ReadEntered => _handler.ReadEntered.Task;

    public IReadOnlyList<string> Order
    {
        get
        {
            lock (_orderGate)
                return _order.ToArray();
        }
    }

    private readonly PumpLog _pumpLog = new();

    private TurnEndGapReadiness(
        SessionStateTestFixture? fixture,
        SecondTranscriptSaveGate? gate,
        SignalingBus? bus,
        int reconnectDelayMs,
        int listTimeoutSeconds = 30)
    {
        _fixture = fixture;
        Gate = gate;
        Bus = bus;
        if (bus is not null)
            bus.OnDelta = () => Note("live-callback");
        SessionId = fixture?.SessionId ?? Guid.NewGuid();
        var settings = new Antiphon.Server.Application.Settings.SessionRunnerSettings
        {
            BaseUrl = "http://runner.invalid",
            Enabled = true,
            EventReconnectDelayMs = reconnectDelayMs,
            EventStreamIdleTimeoutSeconds = 600,
            ListTimeoutSeconds = listTimeoutSeconds,
            RequestTimeoutSeconds = 60,
        };
        _handler = new GapHandler(this, SessionId);
        _http = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        _time = fixture?.Services.GetRequiredService<TimeProvider>() ?? TimeProvider.System;
        _client = new SessionRunnerHttpClient(
            _http,
            new SharedHandlerFactory(_handler),
            Options.Create(settings),
            time: _time);
        if (fixture is null)
            return;

        _counting = new CountingClient(_client);
        var loggerFactory = fixture.Services.GetRequiredService<ILoggerFactory>();
        var runtime = new AgentSessionRuntime(
            _client,
            bus!,
            fixture.Services.GetRequiredService<IOptions<AgentSessionSettings>>(),
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            _time,
            loggerFactory.CreateLogger<AgentSessionRuntime>(),
            states: fixture.Store);
        var scopes = new OverlayScopeFactory(fixture.Services, _counting, runtime);
        _pump = new SessionRunnerEventPump(
            scopes,
            Options.Create(settings),
            _pumpLog,
            _time);
    }

    public static TurnEndGapReadiness CreateStream() => new(null, null, null, 60_000, 30);

    public SecondListFault SecondListFault { get; set; }
    public Task SecondListEntered => _handler.SecondListEntered.Task;
    public int ListCalls => _handler.ListCalls;
    public IReadOnlyCollection<string> PumpWarnings => _pumpLog.Lines.ToArray();
    public Task Disconnected => _pumpLog.Disconnected.Task;

    public void CancelPump() => _stop.Cancel();

    public Task StopPumpAsync() => _pump!.StopAsync(CancellationToken.None);

    public static async Task<TurnEndGapReadiness> CreatePumpAsync(
        bool holdSecondSave = false, int reconnectDelayMs = 60_000, int listTimeoutSeconds = 30)
    {
        var gate = holdSecondSave ? new SecondTranscriptSaveGate() : null;
        var fixture = await SessionStateTestFixture.CreateAsync(fault: gate);
        try
        {
            return new TurnEndGapReadiness(fixture, gate, new SignalingBus(), reconnectDelayMs, listTimeoutSeconds);
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public int MarkersOn(int connection) => _counting?.MarkersOn(connection) ?? 0;

    public void Note(string step)
    {
        lock (_orderGate)
            _order.Add(step);
    }

    public void ReleaseRead() => _handler.ReleaseRead();

    public void FaultRead() => _handler.FaultRead();

    public Task WaitNextReadAsync(CancellationToken ct) => _handler.WaitNextReadAsync(ct);

    public IAsyncEnumerable<SessionRunnerEvent> Stream(CancellationToken ct) => _client.StreamEventsAsync(ct);

    public void CancelWorkers() => _stop.Cancel();

    public Task StartPumpAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _pump!.StartAsync(_stop.Token);
    }

    public async Task<IReadOnlyList<SessionRunnerEvent>> CollectUntilBarrierAsync(CancellationToken ct)
    {
        var events = new List<SessionRunnerEvent>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collect = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in _client.StreamEventsAsync(linked.Token))
                {
                    lock (events)
                        events.Add(evt);
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);

        await WaitNextReadAsync(ct);
        SessionRunnerEvent[] snapshot;
        lock (events)
            snapshot = events.ToArray();
        linked.Cancel();
        ReleaseRead();
        await collect.WaitAsync(ct);
        return snapshot;
    }

    public IReadOnlyList<string> PublicationsWithMarker()
    {
        if (Bus is null)
            return [];
        return Bus.PublishedEvents
            .Select(evt => JsonSerializer.Serialize(evt.Payload, WebJson))
            .Where(payload => payload.Contains(SessionRunnerEvent.SubscriptionReadyName, StringComparison.Ordinal))
            .ToList();
    }

    public async Task<int> CountAsync(string uuid, string kind)
    {
        await using var db = _fixture!.Db();
        return await db.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == SessionId && t.Uuid == uuid && t.Kind == kind);
    }

    public async Task<int> MarkerRowCountAsync()
    {
        await using var db = _fixture!.Db();
        return await db.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == SessionId
            && t.Text != null
            && t.Text.Contains(SessionRunnerEvent.SubscriptionReadyName));
    }

    public void PublishGapEnd(string uuid)
    {
        var origin = new DateTimeOffset(2026, 10, 12, 11, 19, 20, TimeSpan.Zero);
        var entry = Entry(SessionId, 9, TranscriptKinds.TurnEnd, uuid, origin, "end gap", null);
        _handler.Append(entry);
        Note("custody-append");
        var subscribers = SubscribeCount;
        _handler.Hub.Publish(SessionRunnerEventNames.SessionTranscript, entry);
        Note($"hub-publish subscribers={subscribers}");
    }

    public async ValueTask DisposeAsync()
    {
        Gate?.Release.TrySetResult();
        ReleaseRead();
        _handler.EndAll();
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_pump is not null)
        {
            try
            {
                await _pump.StopAsync(CancellationToken.None);
            }
            catch (Exception)
            {
            }
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
        if (_fixture is not null)
            await _fixture.DisposeAsync();
        _stop.Dispose();
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

    private sealed class CountingClient(SessionRunnerHttpClient inner) : ISessionRunnerClient
    {
        private readonly Dictionary<int, int> _markers = [];
        private int _calls;

        public int MarkerCount
        {
            get
            {
                lock (_markers)
                    return _markers.Values.Sum();
            }
        }

        public int MarkersOn(int connection)
        {
            lock (_markers)
                return _markers.TryGetValue(connection, out var count) ? count : 0;
        }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            inner.GetTranscriptAsync(sessionId, ct);

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var connection = Interlocked.Increment(ref _calls);
            await foreach (var evt in inner.StreamEventsAsync(ct))
            {
                if (evt.SubscriptionReady)
                {
                    lock (_markers)
                        _markers[connection] = _markers.TryGetValue(connection, out var count) ? count + 1 : 1;
                }

                yield return evt;
            }
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    public sealed class Connection
    {
        private readonly GapHandler _handler;
        private readonly FrameStream _frames;
        private readonly CancellationTokenSource _subscribe;
        private readonly SessionRunnerEventHub _hub;
        private readonly Action<string> _note;

        internal Connection(
            GapHandler handler, FrameStream frames, CancellationTokenSource subscribe, SessionRunnerEventHub hub, Action<string> note, int number)
        {
            _handler = handler;
            _frames = frames;
            _subscribe = subscribe;
            _hub = hub;
            _note = note;
            Number = number;
        }

        public int Number { get; }

        public Task WriteFrameAsync(string text, bool armNextRead, CancellationToken ct, string? note = null) =>
            _frames.WriteAsync(text, armNextRead, note, ct);

        public void Subscribe()
        {
            _ = _hub.Subscribe(_subscribe.Token);
            _handler.AddSubscriber();
            _subscribe.Token.Register(_handler.RemoveSubscriber);
            _note("hub-subscribed");
        }

        public void End() => _frames.End();
    }

    internal sealed class GapHandler : HttpMessageHandler
    {
        private readonly TurnEndGapReadiness _owner;
        private readonly Guid _sessionId;
        private readonly List<RunnerTranscriptEvent> _snapshot;
        private readonly object _snapshotGate = new();
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callerGets;
        private int _listCalls;
        private int _subscribeCount;
        private int _connections;
        private Task? _producer;
        private FrameStream? _frames;

        public GapHandler(TurnEndGapReadiness owner, Guid sessionId)
        {
            _owner = owner;
            _sessionId = sessionId;
            Hub = new SessionRunnerEventHub();
            var origin = new DateTimeOffset(2026, 10, 12, 11, 19, 16, TimeSpan.Zero);
            _snapshot =
            [
                Entry(sessionId, 1, TranscriptKinds.TurnEnd, "c1205-prior-end", origin, "end prior", null),
                Entry(sessionId, 2, TranscriptKinds.ToolCall, "c1205-tool-call", origin.AddSeconds(1), "tool call", "Read"),
            ];
        }

        public SessionRunnerEventHub Hub { get; }
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ListCalls => Volatile.Read(ref _listCalls);
        public int CallerGets => Volatile.Read(ref _callerGets);
        public int SubscribeCount => Volatile.Read(ref _subscribeCount);
        public bool HeadersRecorded { get; private set; }
        public int HeaderStatus { get; private set; }
        public string? HeaderContentType { get; private set; }
        public bool StreamDisposed => _frames?.DisposedFlag == true;
        public bool ResponseDisposed { get; private set; }
        public bool ContentDisposed { get; private set; }
        public bool SubscriberClosed { get; private set; }
        public Task? Producer => _producer;

        public void AddSubscriber() => Interlocked.Increment(ref _subscribeCount);

        public void RemoveSubscriber() => Interlocked.Decrement(ref _subscribeCount);

        public void Append(RunnerTranscriptEvent entry)
        {
            lock (_snapshotGate)
                _snapshot.Add(entry);
        }

        public void ReleaseRead() => _frames?.Release();

        public void FaultRead() => _frames?.Fault();

        public async Task WaitNextReadAsync(CancellationToken ct)
        {
            await _opened.Task.WaitAsync(ct);
            var frames = _frames ?? throw new InvalidOperationException("The event stream is not open.");
            await frames.WaitNextReadAsync(ct);
        }

        public void EndAll() => _frames?.End();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/sessions")
            {
                var call = Interlocked.Increment(ref _listCalls);
                _owner.Note($"list-{call}");
                if (call == 2 && _owner.SecondListFault != SecondListFault.None)
                {
                    SecondListEntered.TrySetResult();
                    _owner.Note("list-2-entered");
                    if (_owner.SecondListFault == SecondListFault.Throw)
                        throw new HttpRequestException("c1205-second-list-fault");
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                _owner.Note($"list-{call}-ok");
                return Json(new[] { Session(_sessionId) });
            }
            if (path == $"/sessions/{_sessionId:D}/transcript")
            {
                Interlocked.Increment(ref _callerGets);
                _owner.Note("caller-transcript");
                return Json(Snapshot());
            }

            if (path == "/events")
            {
                var number = Interlocked.Increment(ref _connections);
                var subscribe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                subscribe.Token.Register(() => SubscriberClosed = true);
                var frames = new FrameStream(_owner.Note, ReadEntered);
                _frames = frames;
                frames.OnDispose = () => subscribe.Cancel();
                var connection = new Connection(this, frames, subscribe, Hub, _owner.Note, number);
                _opened.TrySetResult();
                var script = _owner.OnConnection ?? ((_, _) => Task.CompletedTask);
                _producer = Task.Run(() => script(connection, _owner._stop.Token), CancellationToken.None);
                var response = new TrackingResponse { StatusCode = HttpStatusCode.OK };
                var content = new TrackingContent(frames);
                content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                response.Content = content;
                response.OnDisposed = () =>
                {
                    ResponseDisposed = true;
                    _owner.Note("iterator-disposed");
                };
                content.OnDisposed = () => ContentDisposed = true;
                HeadersRecorded = true;
                HeaderStatus = (int)response.StatusCode;
                HeaderContentType = content.Headers.ContentType?.MediaType;
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _frames?.Dispose();
            base.Dispose(disposing);
        }

        private RunnerTranscriptDto Snapshot()
        {
            List<RunnerTranscriptEvent> entries;
            lock (_snapshotGate)
                entries = _snapshot.ToList();
            var last = entries.Count == 0 ? 0 : entries[^1].Sequence;
            return new RunnerTranscriptDto(_sessionId, entries, last, false, null);
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

    internal sealed class FrameStream : Stream
    {
        private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly Action<string> _note;
        private readonly TaskCompletionSource _readEntered;
        private readonly object _gate = new();
        private TaskCompletionSource _nextRead = New();
        private TaskCompletionSource _release = New();
        private byte[]? _current;
        private bool _currentArm;
        private bool _barrierPending;
        private int _offset;
        private bool _entered;

        public FrameStream(Action<string> note, TaskCompletionSource readEntered)
        {
            _note = note;
            _readEntered = readEntered;
        }

        public bool DisposedFlag { get; private set; }
        public Action? OnDispose { get; set; }

        public async Task WriteAsync(string text, bool armNextRead, string? note, CancellationToken ct)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _frames.Writer.WriteAsync(new Frame(Encoding.UTF8.GetBytes(text), armNextRead, done, note), ct);
            await done.Task.WaitAsync(ct);
        }

        public void End() => _frames.Writer.TryComplete();

        public void Fault() => _frames.Writer.TryComplete(new IOException("planned read fault"));

        public void Release()
        {
            TaskCompletionSource release;
            lock (_gate)
                release = _release;
            release.TrySetResult();
        }

        public Task WaitNextReadAsync(CancellationToken ct)
        {
            Task task;
            lock (_gate)
                task = _nextRead.Task;
            return task.WaitAsync(ct);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_entered)
            {
                _entered = true;
                _readEntered.TrySetResult();
            }

            if (_barrierPending)
            {
                _barrierPending = false;
                TaskCompletionSource release;
                lock (_gate)
                {
                    _nextRead.TrySetResult();
                    release = _release;
                }

                await release.Task.WaitAsync(cancellationToken);
                lock (_gate)
                {
                    _nextRead = New();
                    _release = New();
                }
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
                catch (ChannelClosedException ex) when (ex.InnerException is IOException fault)
                {
                    throw fault;
                }
                catch (ChannelClosedException)
                {
                    return 0;
                }

                _current = frame.Bytes;
                _currentArm = frame.ArmNextRead;
                _offset = 0;
                _pendingDone = frame.Consumed;
                _pendingNote = frame.Note;
            }

            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            if (_offset >= _current.Length)
            {
                if (_pendingNote is not null)
                    _note(_pendingNote);
                _pendingDone?.TrySetResult();
                _pendingDone = null;
                _pendingNote = null;
                if (_currentArm)
                    _barrierPending = true;
                _current = null;
                _currentArm = false;
            }

            return count;
        }

        private TaskCompletionSource? _pendingDone;
        private string? _pendingNote;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            DisposedFlag = true;
            OnDispose?.Invoke();
            _frames.Writer.TryComplete();
            Release();
            base.Dispose(disposing);
        }

        private static TaskCompletionSource New() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record Frame(byte[] Bytes, bool ArmNextRead, TaskCompletionSource Consumed, string? Note);
    }

    private sealed class TrackingResponse : HttpResponseMessage
    {
        public Action? OnDisposed { get; set; }

        protected override void Dispose(bool disposing)
        {
            OnDisposed?.Invoke();
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingContent : StreamContent
    {
        public TrackingContent(Stream stream) : base(stream)
        {
        }

        public Action? OnDisposed { get; set; }

        protected override void Dispose(bool disposing)
        {
            OnDisposed?.Invoke();
            base.Dispose(disposing);
        }
    }
}

internal sealed class SecondTranscriptSaveGate : SaveChangesInterceptor
{
    private int _transcriptSaves;
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!eventData.Context!.ChangeTracker.Entries<TranscriptEntry>().Any(entry => entry.State == EntityState.Added))
            return result;
        if (Interlocked.Increment(ref _transcriptSaves) == 1)
            return result;
        Reached.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return result;
    }
}

internal enum SecondListFault
{
    None,
    Timeout,
    Throw,
    Cancel,
}

internal sealed class PumpLog : ILogger<SessionRunnerEventPump>
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (level < LogLevel.Warning)
            return;
        var text = formatter(state, exception);
        Lines.Enqueue(text + " " + exception?.GetType().Name);
        if (text.Contains("event stream disconnected", StringComparison.Ordinal))
            Disconnected.TrySetResult();
    }

    public void Dispose()
    {
    }
}

internal sealed class SignalingBus : IEventBus
{
    private readonly MockEventBus _inner = new();
    public TaskCompletionSource Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<MockEventBus.PublishedEvent> PublishedEvents => _inner.PublishedEvents;

    public IReadOnlyList<MockEventBus.PublishedEvent> Deltas() =>
        PublishedEvents.Where(evt => evt.EventName == "AgentTextDelta").ToList();

    public Action? OnDelta { get; set; }

    public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
    {
        var task = _inner.PublishToGroupAsync(group, eventName, payload, ct);
        if (eventName == "AgentTextDelta")
        {
            OnDelta?.Invoke();
            Seen.TrySetResult();
        }

        return task;
    }

    public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
        _inner.PublishToAllAsync(eventName, payload, ct);
}

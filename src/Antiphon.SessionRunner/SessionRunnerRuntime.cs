using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

public sealed class SessionRunnerRuntime
{
    private readonly ConcurrentDictionary<Guid, RunnerSession> _sessions = new();
    private readonly SessionRunnerEventHub _events = new();
    private readonly SessionRunnerSettings _settings;
    private readonly ILogger<SessionRunnerRuntime> _logger;

    public SessionRunnerRuntime(
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerRuntime> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
    {
        if (request.SessionId == Guid.Empty)
            throw new ArgumentException("SessionId must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Exe))
            throw new ArgumentException("Exe must not be empty.", nameof(request));
        if (request.Cols <= 0 || request.Rows <= 0)
            throw new ArgumentException("Terminal size must be positive.", nameof(request));
        if (request.MemoryLimitMb < 0)
            throw new ArgumentException("MemoryLimitMb must not be negative.", nameof(request));

        var session = new RunnerSession(request.SessionId, _settings, _events, _logger);
        if (!_sessions.TryAdd(request.SessionId, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException($"Session '{request.SessionId}' is already running.");
        }

        try
        {
            await session.StartAsync(request, ct);
            return session.ToDto();
        }
        catch
        {
            _sessions.TryRemove(request.SessionId, out _);
            await session.DisposeAsync();
            throw;
        }
    }

    public IReadOnlyList<RunnerSessionDto> List() =>
        _sessions.Values.Select(session => session.ToDto()).OrderBy(session => session.StartedAt).ToList();

    public RunnerSessionDto Get(Guid sessionId) => GetSession(sessionId).ToDto();

    public RunnerBufferDto GetBuffer(Guid sessionId) => GetSession(sessionId).GetBuffer();

    public RunnerSnapshotDto GetSnapshot(Guid sessionId) => GetSession(sessionId).GetSnapshot();

    public RunnerTranscriptDto GetTranscript(Guid sessionId) => GetSession(sessionId).GetTranscript();

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        string.IsNullOrEmpty(input)
            ? Task.CompletedTask
            : GetSession(sessionId).WriteAsync(input, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        GetSession(sessionId).ClearLiveBuffer();
        return Task.CompletedTask;
    }

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        if (cols <= 0 || rows <= 0)
            throw new ArgumentException("Terminal size must be positive.");

        GetSession(sessionId).Resize(cols, rows);
        return Task.CompletedTask;
    }

    public async Task<RunnerSessionDto> KillAsync(Guid sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var session = GetSession(sessionId);
        await session.KillAsync(timeout, ct);
        return session.ToDto();
    }

    public ChannelReader<RunnerServerSentEvent> Subscribe(CancellationToken ct) => _events.Subscribe(ct);

    private RunnerSession GetSession(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session)
            ? session
            : throw new KeyNotFoundException($"Session '{sessionId}' was not found.");

    private sealed class RunnerSession : IAsyncDisposable
    {
        private readonly Guid _sessionId;
        private readonly RunnerBuffer _buffer;
        private readonly SessionRunnerEventHub _events;
        private readonly ILogger _logger;
        private readonly PtyAgentRunner _runner = new();
        private readonly object _gate = new();
        private TranscriptTailer? _tailer;
        private long _lastSequence;
        private string _status = "Starting";
        private int? _exitCode;
        private string _exitReason = PtyExitReason.Unknown.ToString();

        public RunnerSession(
            Guid sessionId,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger)
        {
            _sessionId = sessionId;
            _buffer = new RunnerBuffer(sessionId, Math.Max(1, settings.ReplayBufferMaxChars), settings.SessionLogPath);
            _events = events;
            _logger = logger;
        }

        public async Task StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            _runner.OnData += OnData;
            await _runner.StartAsync(
                request.Exe,
                request.Args.ToArray(),
                request.Cwd,
                request.Env.ToDictionary(kv => kv.Key, kv => kv.Value),
                request.Cols,
                request.Rows,
                request.MemoryLimitMb,
                ct);

            lock (_gate)
            {
                _status = "Running";
            }

            _events.Publish(
                SessionRunnerEventNames.SessionStarted,
                new RunnerSessionStartedEvent(_sessionId, _runner.Pid, _runner.StartedAt));

            if (request.TranscriptEnabled)
            {
                _tailer = new TranscriptTailer(_sessionId, request.Cwd, _events, _logger);
                _tailer.Start();
            }

            _ = ObserveExitAsync();
        }

        public RunnerSessionDto ToDto()
        {
            lock (_gate)
            {
                return new RunnerSessionDto(
                    _sessionId,
                    _runner.Pid,
                    _runner.StartedAt,
                    _status,
                    _exitCode,
                    _exitReason,
                    _lastSequence);
            }
        }

        public RunnerBufferDto GetBuffer()
        {
            lock (_gate)
            {
                return new RunnerBufferDto(_sessionId, _buffer.FullSnapshot(), _lastSequence);
            }
        }

        public RunnerSnapshotDto GetSnapshot()
        {
            lock (_gate)
            {
                return new RunnerSnapshotDto(
                    _sessionId,
                    _runner.SnapshotText(),
                    _runner.SnapshotScreen(),
                    _lastSequence,
                    _runner.StartedAt);
            }
        }

        public RunnerTranscriptDto GetTranscript() =>
            _tailer?.Snapshot() ?? new RunnerTranscriptDto(_sessionId, Array.Empty<RunnerTranscriptEvent>(), 0);

        public Task WriteAsync(string input, CancellationToken ct) => _runner.WriteAsync(input, ct);

        public void ClearLiveBuffer() => _runner.ClearLiveBuffer();

        public void Resize(int cols, int rows) => _runner.Resize(cols, rows);

        public async Task KillAsync(TimeSpan timeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await _runner.KillAsync(timeout);
        }

        public async ValueTask DisposeAsync()
        {
            _runner.OnData -= OnData;
            if (_tailer is not null)
                await _tailer.DisposeAsync();
            await _runner.DisposeAsync();
        }

        private void OnData(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            long sequence;
            lock (_gate)
            {
                sequence = ++_lastSequence;
                _buffer.Append(text);
            }

            _events.Publish(
                SessionRunnerEventNames.SessionOutput,
                new RunnerOutputEvent(_sessionId, sequence, text));
        }

        private async Task ObserveExitAsync()
        {
            try
            {
                var exitCode = await _runner.Exited;
                lock (_gate)
                {
                    _status = "Exited";
                    _exitCode = exitCode;
                    _exitReason = _runner.ExitReason.ToString();
                }

                _events.Publish(
                    SessionRunnerEventNames.SessionExited,
                    new RunnerSessionExitedEvent(_sessionId, exitCode, _runner.ExitReason.ToString(), _lastSequence));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed while observing runner session {SessionId} exit", _sessionId);
                _events.Publish(
                    SessionRunnerEventNames.SessionError,
                    new { sessionId = _sessionId, message = ex.Message });
            }
        }
    }

    private sealed class RunnerBuffer
    {
        private readonly StringBuilder _buffer = new();
        private readonly int _maxChars;
        private readonly string _filePath;

        public RunnerBuffer(Guid sessionId, int maxChars, string logRoot)
        {
            _maxChars = maxChars;
            Directory.CreateDirectory(logRoot);
            _filePath = Path.Combine(logRoot, $"{sessionId:N}.ansi.log");
            Hydrate();
        }

        public void Append(string text)
        {
            File.AppendAllText(_filePath, text);
            _buffer.Append(text);
            if (_buffer.Length <= _maxChars)
                return;

            _buffer.Remove(0, _buffer.Length - _maxChars);
        }

        public string FullSnapshot() =>
            File.Exists(_filePath)
                ? File.ReadAllText(_filePath)
                : _buffer.ToString();

        private void Hydrate()
        {
            if (!File.Exists(_filePath))
                return;

            var text = File.ReadAllText(_filePath);
            if (text.Length > _maxChars)
                text = text[^_maxChars..];

            _buffer.Append(text);
        }
    }
}

public sealed record RunnerServerSentEvent(string EventName, string Json);

public sealed class SessionRunnerEventHub
{
    private readonly object _gate = new();
    private readonly List<Channel<RunnerServerSentEvent>> _subscribers = [];

    public ChannelReader<RunnerServerSentEvent> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<RunnerServerSentEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate)
            _subscribers.Add(channel);

        ct.Register(() =>
        {
            lock (_gate)
                _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public void Publish<T>(string eventName, T payload)
    {
        var evt = new RunnerServerSentEvent(eventName, System.Text.Json.JsonSerializer.Serialize(payload));
        Channel<RunnerServerSentEvent>[] subscribers;
        lock (_gate)
            subscribers = [.. _subscribers];

        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(evt);
    }
}

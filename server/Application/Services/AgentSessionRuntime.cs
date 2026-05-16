using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class AgentSessionRuntime
{
    private readonly ConcurrentDictionary<Guid, RuntimeSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, RuntimeBuffer> _buffers = new();
    private readonly IEventBus _eventBus;
    private readonly AgentSessionSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentSessionRuntime> _logger;
    private readonly AgentMentionRouter? _mentionRouter;

    public AgentSessionRuntime(
        IEventBus eventBus,
        IOptions<AgentSessionSettings> settings,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AgentSessionRuntime> logger,
        AgentMentionRouter? mentionRouter = null)
    {
        _eventBus = eventBus;
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _mentionRouter = mentionRouter;
    }

    public void Register(Guid sessionId, IAgentProtocolAdapter adapter)
    {
        var buffer = _buffers.GetOrAdd(sessionId, CreateBuffer);
        var session = new RuntimeSession(
            sessionId,
            adapter,
            buffer,
            _eventBus,
            _settings,
            _logger,
            RecordActivityAsync,
            _mentionRouter);
        adapter.OnTextDelta += session.OnTextDelta;

        if (!_sessions.TryAdd(sessionId, session))
        {
            adapter.OnTextDelta -= session.OnTextDelta;
            throw new ConflictException($"Agent session '{sessionId}' is already running.");
        }
    }

    public async Task<bool> WaitForFirstDeltaAsync(Guid sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var session = GetSession(sessionId);
        var completed = await Task.WhenAny(
            session.FirstDelta.Task,
            Task.Delay(timeout, ct));

        return completed == session.FirstDelta.Task;
    }

    public long GetDeltaSequence(Guid sessionId) => GetSession(sessionId).DeltaSequence;

    public long GetDeltaSequenceOrDefault(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session.DeltaSequence : 0;

    public async Task<bool> WaitForDeltaAfterAsync(
        Guid sessionId,
        long sequence,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var session = GetSession(sessionId);
        if (session.DeltaSequence > sequence)
            return true;

        var delay = Task.Delay(timeout, ct);
        while (!delay.IsCompleted)
        {
            if (session.DeltaSequence > sequence)
                return true;

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
        }

        return session.DeltaSequence > sequence;
    }

    public AgentSessionRuntimeBufferSnapshot GetBufferSnapshot(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            return session.GetBufferSnapshot();

        var buffer = _buffers.GetOrAdd(sessionId, CreateBuffer);
        return new AgentSessionRuntimeBufferSnapshot(buffer.FullSnapshot(), 0);
    }

    public IReadOnlyList<Guid> ListLiveSessions() => _sessions.Keys.ToList();

    public bool TryGetLiveSnapshot(Guid sessionId, out AgentSessionLiveSnapshot snapshot)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            try
            {
                snapshot = session.GetLiveSnapshot();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Live snapshot unavailable for agent session {SessionId}", sessionId);
            }
        }

        snapshot = default!;
        return false;
    }

    public bool TryGetLiveMetadata(Guid sessionId, out AgentSessionLiveMetadata metadata)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            metadata = new AgentSessionLiveMetadata(sessionId, session.DeltaSequence);
            return true;
        }

        metadata = default!;
        return false;
    }

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        var session = GetSession(sessionId);
        if (string.IsNullOrEmpty(input))
            return;

        await session.Adapter.SendInputAsync(input, ct);
    }

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        GetSession(sessionId).Adapter.ResizeAsync(cols, rows, ct);

    public Task<bool> KillAsync(Guid sessionId, TimeSpan timeout, CancellationToken ct) =>
        GetSession(sessionId).Adapter.KillAsync(timeout, ct);

    public bool TryRemove(Guid sessionId, out IAgentProtocolAdapter? adapter)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            adapter = null;
            return false;
        }

        session.Adapter.OnTextDelta -= session.OnTextDelta;
        session.Stop();
        adapter = session.Adapter;
        return true;
    }

    public async Task DisposeSessionAsync(Guid sessionId)
    {
        if (TryRemove(sessionId, out var adapter) && adapter is not null)
            await adapter.DisposeAsync();
    }

    private async Task RecordActivityAsync(Guid sessionId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is not null)
            {
                session.LastSeenAt = now;
            }

            var attempt = await db.RunAttempts
                .Where(a => a.AgentSessionId == sessionId
                    && a.Phase == RunPhase.StreamingTurn
                    && a.CompletedAt == null)
                .OrderByDescending(a => a.AttemptNumber)
                .FirstOrDefaultAsync();
            if (attempt is not null)
            {
                attempt.LastEventAt = now;
            }

            if (session is not null || attempt is not null)
                await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity for agent session {SessionId}", sessionId);
        }
    }

    private RuntimeSession GetSession(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            return session;

        throw new NotFoundException("AgentSessionRuntime", sessionId);
    }

    private RuntimeBuffer CreateBuffer(Guid sessionId) =>
        new(sessionId, Math.Max(1, _settings.ReplayBufferMaxChars), _settings.SessionLogPath);

    private sealed class RuntimeSession
    {
        private readonly Guid _sessionId;
        private readonly RuntimeBuffer _buffer;
        private readonly IEventBus _eventBus;
        private readonly AgentSessionSettings _settings;
        private readonly ILogger _logger;
        private readonly Func<Guid, Task> _recordActivityAsync;
        private readonly AgentMentionRouter? _mentionRouter;
        private readonly Channel<RuntimeDelta> _deltas = Channel.CreateUnbounded<RuntimeDelta>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        private readonly object _gate = new();
        private long _deltaSequence;

        public RuntimeSession(
            Guid sessionId,
            IAgentProtocolAdapter adapter,
            RuntimeBuffer buffer,
            IEventBus eventBus,
            AgentSessionSettings settings,
            ILogger logger,
            Func<Guid, Task> recordActivityAsync,
            AgentMentionRouter? mentionRouter)
        {
            _sessionId = sessionId;
            Adapter = adapter;
            _buffer = buffer;
            _eventBus = eventBus;
            _settings = settings;
            _logger = logger;
            _recordActivityAsync = recordActivityAsync;
            _mentionRouter = mentionRouter;
            _ = ProcessDeltasAsync();
        }

        public IAgentProtocolAdapter Adapter { get; }
        public TaskCompletionSource FirstDelta { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long DeltaSequence => Interlocked.Read(ref _deltaSequence);

        public void OnTextDelta(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var deltas = new List<RuntimeDelta>();
            var maxChunkChars = Math.Max(1, _settings.SignalRMaxChunkChars);
            lock (_gate)
            {
                for (var offset = 0; offset < text.Length; offset += maxChunkChars)
                {
                    var chunk = text.Substring(offset, Math.Min(maxChunkChars, text.Length - offset));
                    var deltaSequence = ++_deltaSequence;
                    _buffer.Append(chunk);
                    deltas.Add(new RuntimeDelta(deltaSequence, chunk));
                }
            }

            foreach (var delta in deltas)
            {
                try
                {
                    _mentionRouter?.ObserveDelta(_sessionId, delta.Text);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to observe mention delta for session {SessionId}", _sessionId);
                }
            }

            FirstDelta.TrySetResult();
            foreach (var delta in deltas)
            {
                if (!_deltas.Writer.TryWrite(delta))
                    _logger.LogWarning("Failed to enqueue PTY delta for session {SessionId}", _sessionId);
            }
        }

        public AgentSessionRuntimeBufferSnapshot GetBufferSnapshot()
        {
            lock (_gate)
            {
                return new AgentSessionRuntimeBufferSnapshot(_buffer.FullSnapshot(), _deltaSequence);
            }
        }

        public AgentSessionLiveSnapshot GetLiveSnapshot() =>
            new(
                _sessionId,
                string.Empty,
                Adapter.SnapshotRenderedScreen(),
                _buffer.TailSnapshot(Math.Max(1, _settings.ReplayBufferMaxChars)),
                DeltaSequence);

        public void Stop()
        {
            _deltas.Writer.TryComplete();
        }

        private async Task ProcessDeltasAsync()
        {
            await foreach (var delta in _deltas.Reader.ReadAllAsync())
            {
                try
                {
                    await _eventBus.PublishToGroupAsync(
                        AgentSessionGroups.Session(_sessionId),
                        "AgentTextDelta",
                        new
                        {
                            sessionId = _sessionId,
                            sequence = delta.DeltaSequence,
                            text = delta.Text
                        });

                    await _recordActivityAsync(_sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish AgentTextDelta for session {SessionId}", _sessionId);
                }
            }
        }
    }

    private sealed class RuntimeBuffer
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();
        private readonly int _maxChars;
        private readonly string _filePath;

        public RuntimeBuffer(Guid sessionId, int maxChars, string logRoot)
        {
            _maxChars = maxChars;
            Directory.CreateDirectory(logRoot);
            _filePath = Path.Combine(logRoot, $"{sessionId:N}.ansi.log");
            Hydrate();
        }

        public void Append(string text)
        {
            lock (_gate)
            {
                File.AppendAllText(_filePath, text);
                _buffer.Append(text);
                if (_buffer.Length <= _maxChars)
                    return;

                _buffer.Remove(0, _buffer.Length - _maxChars);
            }
        }

        public string FullSnapshot()
        {
            lock (_gate)
            {
                if (File.Exists(_filePath))
                    return File.ReadAllText(_filePath);

                return _buffer.ToString();
            }
        }

        public string TailSnapshot(int maxChars)
        {
            lock (_gate)
            {
                var text = _buffer.ToString();
                return text.Length > maxChars ? text[^maxChars..] : text;
            }
        }

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

    private sealed record RuntimeDelta(long DeltaSequence, string Text);
}

public sealed record AgentSessionRuntimeBufferSnapshot(string Buffer, long LastSequence);

public sealed record AgentSessionLiveSnapshot(
    Guid SessionId,
    string RawOutput,
    string RenderedScreen,
    string Buffer,
    long LastSequence);

public sealed record AgentSessionLiveMetadata(Guid SessionId, long LastSequence);

public static class AgentSessionGroups
{
    public static string Session(Guid sessionId) => $"session-{sessionId}";
}

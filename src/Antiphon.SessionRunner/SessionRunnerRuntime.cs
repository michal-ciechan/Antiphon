using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// Session registry and orchestration. Since the pty-host split, the runner does NOT own ConPTY
/// processes: each session's child lives in a detached per-session Antiphon.PtyHost process, and
/// the runner talks to it over a named pipe. The runner keeps all interpretation (screen render,
/// transcripts, events) so it can be restarted freely without killing a single session.
/// </summary>
public sealed class SessionRunnerRuntime : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, RunnerSession> _sessions = new();
    private readonly SessionRunnerEventHub _events = new();
    private readonly SessionRunnerSettings _settings;
    private readonly PtyHostLauncher _launcher;
    private readonly ILogger<SessionRunnerRuntime> _logger;

    public SessionRunnerRuntime(
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerRuntime> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _launcher = new PtyHostLauncher(
            new ShadowCopyStore(_settings.PtyHostBinDir),
            _settings.ResolvedPtyHostSourceDir);
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
            // A session id can be relaunched once its process has exited (claude --resume reuses
            // the original id); only a live session blocks the id.
            if (_sessions.TryGetValue(request.SessionId, out var existing)
                && existing.HasExited
                && _sessions.TryUpdate(request.SessionId, session, existing))
            {
                await existing.DisposeAsync();
            }
            else
            {
                await session.DisposeAsync();
                throw new InvalidOperationException($"Session '{request.SessionId}' is already running.");
            }
        }

        try
        {
            await session.StartAsync(request, _launcher, ct);
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
        return GetSession(sessionId).ClearLiveBufferAsync(ct);
    }

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        if (cols <= 0 || rows <= 0)
            throw new ArgumentException("Terminal size must be positive.");

        return GetSession(sessionId).ResizeAsync(cols, rows, ct);
    }

    public async Task<RunnerSessionDto> KillAsync(Guid sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var session = GetSession(sessionId);
        await session.KillAsync(timeout, ct);
        return session.ToDto();
    }

    public ChannelReader<RunnerServerSentEvent> Subscribe(CancellationToken ct) => _events.Subscribe(ct);

    /// <summary>
    /// Startup adoption sweep: reconnects to pty-hosts that survived a runner restart. MUST run
    /// to completion before the HTTP API starts listening - the server's reconciler treats "the
    /// runner doesn't know this session" as fatal, so the runner may never serve a half-adopted
    /// session list. For each manifest on disk:
    /// live host  -> reconnect, rebuild interpretation from the ansi log, resume streaming;
    /// exited host-> collect the recorded exit, publish the missed SessionExited, ack Shutdown;
    /// dead host  -> register the session as Exited with whatever fate the manifest recorded.
    /// </summary>
    public async Task<int> AdoptOrphanedHostsAsync(IProcessLivenessProbe probe, CancellationToken ct)
    {
        var manifestDir = _settings.PtyHostManifestDir;
        if (!Directory.Exists(manifestDir))
            return 0;

        var adopted = 0;
        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var manifest = PtyHostManifest.TryLoad(file);
            if (manifest is null || manifest.SessionId == Guid.Empty)
            {
                TryDeleteFile(file);
                continue;
            }

            if (_sessions.ContainsKey(manifest.SessionId))
                continue;

            if (probe.IsAlive(manifest.HostPid, manifest.HostStartTimeUtc))
            {
                var session = new RunnerSession(manifest.SessionId, _settings, _events, _logger);
                try
                {
                    var running = await session.AdoptAsync(manifest, ct);
                    _sessions.TryAdd(manifest.SessionId, session);
                    adopted++;
                    _logger.LogInformation(
                        "Adopted pty-host for session {SessionId} (host pid {HostPid}, {State})",
                        manifest.SessionId, manifest.HostPid, running ? "running" : "exited while runner was down");
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Live pty-host for session {SessionId} (pid {HostPid}) could not be adopted; treating as dead",
                        manifest.SessionId, manifest.HostPid);
                    await session.DisposeAsync();
                    KillPidBestEffort(manifest.HostPid);
                }
            }

            // Dead (or unadoptable) host: the ConPTY died with it, so the child is gone too.
            // Register the session as Exited with the fate the manifest recorded so the server
            // sees a real exit instead of an unknown session.
            var exitedSession = RunnerSession.CreateAdoptedExited(manifest, _settings, _events, _logger);
            _sessions.TryAdd(manifest.SessionId, exitedSession);
            TryDeleteFile(file);
            _logger.LogWarning(
                "pty-host for session {SessionId} (pid {HostPid}) is gone; registered as Exited ({Reason})",
                manifest.SessionId, manifest.HostPid, exitedSession.ToDto().ExitReason);
        }

        return adopted;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort; a re-read next startup lands in the same branch.
        }
    }

    private static void KillPidBestEffort(int pid)
    {
        try
        {
            System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }

    /// <summary>
    /// Marks every "Running" session whose OS process is gone as Exited (reason ProcessVanished)
    /// and publishes the missed SessionExited event. This is the liveness backstop for exits the
    /// normal observer never saw — a session once sat "Running" on a dead PID for a week, keeping
    /// its agent badged Working in the UI with no process behind it. Returns the ids it marked.
    /// </summary>
    public IReadOnlyList<Guid> SweepVanishedSessions(IProcessLivenessProbe probe)
    {
        var marked = new List<Guid>();
        foreach (var (sessionId, session) in _sessions)
        {
            if (session.MarkVanishedIfDead(probe))
            {
                _logger.LogWarning(
                    "Liveness sweep marked session {SessionId} as Exited: its process vanished without an exit event",
                    sessionId);
                marked.Add(sessionId);
            }
        }

        return marked;
    }

    /// <summary>
    /// Detaches from every host WITHOUT killing anything - sessions keep running in their
    /// detached hosts and are re-adopted by the next runner via <see cref="AdoptOrphanedHostsAsync"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var (sessionId, session) in _sessions)
        {
            _sessions.TryRemove(sessionId, out _);
            await session.DisposeAsync();
        }
    }

    private RunnerSession GetSession(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session)
            ? session
            : throw new KeyNotFoundException($"Session '{sessionId}' was not found.");

    private sealed class RunnerSession : IAsyncDisposable
    {
        private readonly Guid _sessionId;
        private readonly SessionRunnerSettings _settings;
        private readonly SessionRunnerEventHub _events;
        private readonly ILogger _logger;
        private readonly object _gate = new();
        private readonly StringBuilder _liveBuffer = new();
        private readonly TaskCompletionSource _exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private PtyHostClient? _client;
        private int _hostPid;
        private int? _childPid;
        private DateTime _startedAt;
        private TerminalScreen? _screen;
        private string? _ansiLogPath;
        private TranscriptTailer? _tailer;
        private long _lastSequence;
        private string _status = "Starting";
        private int? _exitCode;
        private string _exitReason = PtyExitReason.Unknown.ToString();
        private bool _adopted;

        public RunnerSession(
            Guid sessionId,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger)
        {
            _sessionId = sessionId;
            _settings = settings;
            _events = events;
            _logger = logger;
        }

        public DateTime StartedAt => _startedAt;

        public async Task StartAsync(RunnerLaunchRequest request, PtyHostLauncher launcher, CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SessionLogPath);
            Directory.CreateDirectory(_settings.PtyHostLogDir);
            _ansiLogPath = Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(request.Cols, request.Rows);

            _hostPid = await launcher.LaunchDetachedAsync(
                _sessionId,
                _settings.PtyHostManifestDir,
                hostLogFile: Path.Combine(_settings.PtyHostLogDir, $"{_sessionId:N}.log"),
                launchTimeout: TimeSpan.FromSeconds(_settings.PtyHostLaunchTimeoutSec),
                lingerTtl: TimeSpan.FromHours(_settings.PtyHostLingerHours),
                ringCapChars: Math.Max(1, _settings.ReplayBufferMaxChars),
                ct: ct);

            try
            {
                _client = await PtyHostClient.ConnectAsync(
                    PtyHostProtocol.PipeNameFor(_sessionId),
                    TimeSpan.FromSeconds(_settings.PtyHostConnectTimeoutSec),
                    ct);
                _client.OnOutput += HandleOutput;
                _client.OnExited += HandleExited;
                _client.OnDisconnected += HandleDisconnected;

                var launched = await _client.LaunchAsync(
                    new LaunchMessage(
                        request.Exe,
                        request.Args,
                        request.Env,
                        request.Cwd,
                        request.Cols,
                        request.Rows,
                        request.MemoryLimitMb,
                        request.TranscriptEnabled,
                        _ansiLogPath),
                    ct);

                _childPid = launched.ChildPid;
                _startedAt = launched.ChildStartTimeUtc;
                lock (_gate)
                {
                    _status = "Running";
                }

                _events.Publish(
                    SessionRunnerEventNames.SessionStarted,
                    new RunnerSessionStartedEvent(_sessionId, _childPid, _startedAt));

                if (await _client.AttachAsync(0, ct) is { } resync)
                {
                    // Impossible on a fresh host (nothing can have left the ring yet) — but if it
                    // ever happens, the ansi log still has everything; log and continue live.
                    _logger.LogWarning(
                        "Fresh session {SessionId} answered attach with resync ({First}..{Last})",
                        _sessionId, resync.FirstAvailableSeq, resync.LastSeq);
                    await _client.AttachAsync(resync.LastSeq, ct);
                }

                if (request.TranscriptEnabled)
                {
                    _tailer = new TranscriptTailer(_sessionId, request.Cwd, _events, _logger);
                    _tailer.Start();
                }
            }
            catch
            {
                // Never leave an orphaned empty host behind a failed start.
                KillHostBestEffort();
                throw;
            }
        }

        /// <summary>
        /// Re-attach to a host that survived a runner restart. Rebuilds runner-side interpretation
        /// (screen, live buffer) from the ansi log tail, resumes live streaming at the host's
        /// sequence, and - if the child exited while the runner was down - publishes the missed
        /// SessionExited and acks Shutdown. Returns true if the session is still running.
        /// </summary>
        public async Task<bool> AdoptAsync(PtyHostManifest manifest, CancellationToken ct)
        {
            _hostPid = manifest.HostPid;
            _childPid = manifest.ChildPid;
            _adopted = true;
            _startedAt = manifest.ChildStartTimeUtc ?? manifest.CreatedAtUtc;
            _ansiLogPath = manifest.AnsiLogPath
                ?? Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(
                manifest.Cols > 0 ? manifest.Cols : 120,
                manifest.Rows > 0 ? manifest.Rows : 30);

            _client = await PtyHostClient.ConnectAsync(manifest.PipeName, TimeSpan.FromSeconds(5), ct);
            _client.OnOutput += HandleOutput;
            _client.OnExited += HandleExited;
            _client.OnDisconnected += HandleDisconnected;

            var status = await _client.GetStatusAsync(ct);
            _childPid = status.ChildPid ?? _childPid;
            RebuildInterpretationFromAnsiLog(status.LastSeq);

            if (status.Status == PtyHostStatus.Exited)
            {
                // HandleExited publishes the missed event and acks Shutdown.
                HandleExited(new ExitedMessage(status.ExitCode, status.ExitReason ?? "Unknown", status.LastSeq));
                return false;
            }

            lock (_gate)
            {
                _status = "Running";
            }

            var attachAt = status.LastSeq;
            for (var attempt = 0; ; attempt++)
            {
                if (await _client.AttachAsync(attachAt, ct) is not { } resync)
                    break;

                // Output flooded past the ring between Status and Attach; the ansi log has it all.
                if (attempt >= 3)
                    throw new InvalidOperationException(
                        $"Session {_sessionId}: attach kept resyncing (ring {resync.FirstAvailableSeq}..{resync.LastSeq}).");
                RebuildInterpretationFromAnsiLog(resync.LastSeq);
                attachAt = resync.LastSeq;
            }

            _events.Publish(
                SessionRunnerEventNames.SessionAdopted,
                new RunnerSessionAdoptedEvent(_sessionId, _childPid, _startedAt, status.LastSeq));

            if (manifest.TranscriptEnabled)
            {
                _tailer = new TranscriptTailer(_sessionId, manifest.Cwd ?? "", _events, _logger);
                _tailer.Start();
            }

            return true;
        }

        /// <summary>
        /// Registers a session whose host is gone: the fate is whatever the manifest recorded
        /// (a real exit while the runner was down, or ProcessVanished when the host died cold).
        /// Publishes the missed SessionExited so late subscribers reconcile off the registry.
        /// </summary>
        public static RunnerSession CreateAdoptedExited(
            PtyHostManifest manifest,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger)
        {
            var session = new RunnerSession(manifest.SessionId, settings, events, logger)
            {
                _hostPid = manifest.HostPid,
                _childPid = manifest.ChildPid,
                _startedAt = manifest.ChildStartTimeUtc ?? manifest.CreatedAtUtc,
                _ansiLogPath = manifest.AnsiLogPath,
                _adopted = true,
                _status = "Exited",
                _exitCode = manifest.ExitCode ?? -1,
                _exitReason = manifest.ExitReason ?? "ProcessVanished",
            };
            session._exited.TrySetResult();

            events.Publish(
                SessionRunnerEventNames.SessionExited,
                new RunnerSessionExitedEvent(
                    manifest.SessionId, session._exitCode, session._exitReason, LastSequence: 0));
            return session;
        }

        private void RebuildInterpretationFromAnsiLog(long lastSeq)
        {
            var replay = ReadAnsiLog();
            var cap = Math.Max(1, _settings.ReplayBufferMaxChars);
            if (replay.Length > cap)
                replay = replay[^cap..];

            lock (_gate)
            {
                _liveBuffer.Clear();
                _liveBuffer.Append(replay);
                _screen?.Feed(replay);
                _lastSequence = Math.Max(_lastSequence, lastSeq);
            }
        }

        public bool HasExited
        {
            get
            {
                lock (_gate)
                    return _status == "Exited";
            }
        }

        public RunnerSessionDto ToDto()
        {
            lock (_gate)
            {
                return new RunnerSessionDto(
                    _sessionId,
                    _childPid,
                    _startedAt,
                    _status,
                    _exitCode,
                    _exitReason,
                    _lastSequence,
                    _hostPid > 0 ? _hostPid : null,
                    _adopted);
            }
        }

        public RunnerBufferDto GetBuffer()
        {
            long lastSequence;
            lock (_gate)
                lastSequence = _lastSequence;
            return new RunnerBufferDto(_sessionId, ReadAnsiLog(), lastSequence);
        }

        public RunnerSnapshotDto GetSnapshot()
        {
            lock (_gate)
            {
                return new RunnerSnapshotDto(
                    _sessionId,
                    _liveBuffer.ToString(),
                    _screen?.GetScreenText() ?? "",
                    _lastSequence,
                    _startedAt);
            }
        }

        public RunnerTranscriptDto GetTranscript() =>
            _tailer?.Snapshot() ?? new RunnerTranscriptDto(_sessionId, Array.Empty<RunnerTranscriptEvent>(), 0);

        public Task WriteAsync(string input, CancellationToken ct) =>
            RequireClient().InputAsync(input, ct);

        public async Task ClearLiveBufferAsync(CancellationToken ct)
        {
            lock (_gate)
                _liveBuffer.Clear();
            if (_client is { } client)
                await client.ClearLiveBufferAsync(ct);
        }

        public Task ResizeAsync(int cols, int rows, CancellationToken ct) =>
            RequireClient().ResizeAsync(cols, rows, ct);

        public async Task KillAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (HasExited || _client is not { } client)
                return;

            await client.KillAsync(timeout, ct);
            // Parity with the old in-proc KillAsync: wait for the exit (with a grace margin for
            // the pipe round-trip); the liveness sweep is the backstop if it never arrives.
            await Task.WhenAny(_exited.Task, Task.Delay(timeout + TimeSpan.FromSeconds(2), ct));
        }

        /// <summary>
        /// Liveness backstop: if this session claims Running but its OS process is gone, transition
        /// to Exited and publish the SessionExited event the normal observer missed. Idempotent and
        /// race-safe: re-checks the status under the gate before transitioning.
        /// </summary>
        public bool MarkVanishedIfDead(IProcessLivenessProbe probe)
        {
            int? pid;
            DateTime startedAt;
            lock (_gate)
            {
                if (_status != "Running")
                    return false;
                pid = _childPid;
                startedAt = _startedAt;
            }

            if (pid is int livePid && probe.IsAlive(livePid, startedAt))
                return false;

            long lastSequence;
            int? exitCode;
            lock (_gate)
            {
                if (_status != "Running")
                    return false; // a real exit event won the race — keep its verdict
                _status = "Exited";
                _exitCode ??= -1;
                _exitReason = "ProcessVanished";
                exitCode = _exitCode;
                lastSequence = _lastSequence;
            }

            _events.Publish(
                SessionRunnerEventNames.SessionExited,
                new RunnerSessionExitedEvent(_sessionId, exitCode, "ProcessVanished", lastSequence));
            _exited.TrySetResult();

            // The session is declared dead; the host (if any survives) has no further purpose.
            _ = Task.Run(ShutdownHostAsync);
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            // Dispose detaches from the host — it must NOT kill it: surviving the runner's own
            // teardown is the entire point of the pty-host split.
            if (_client is { } client)
            {
                _client = null;
                client.OnOutput -= HandleOutput;
                client.OnExited -= HandleExited;
                client.OnDisconnected -= HandleDisconnected;
                await client.DisposeAsync();
            }

            if (_tailer is not null)
                await _tailer.DisposeAsync();
        }

        private void HandleOutput(long seq, string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
                return;

            lock (_gate)
            {
                _lastSequence = Math.Max(_lastSequence, seq);
                _liveBuffer.Append(chunk);
                _screen?.Feed(chunk);
            }

            _events.Publish(
                SessionRunnerEventNames.SessionOutput,
                new RunnerOutputEvent(_sessionId, seq, chunk));
        }

        private void HandleExited(ExitedMessage exited)
        {
            bool transitioned;
            lock (_gate)
            {
                transitioned = _status != "Exited";
                if (transitioned)
                {
                    _status = "Exited";
                    _exitCode = exited.ExitCode;
                    _exitReason = exited.ExitReason;
                    _lastSequence = Math.Max(_lastSequence, exited.LastSeq);
                }
            }

            if (transitioned)
            {
                _events.Publish(
                    SessionRunnerEventNames.SessionExited,
                    new RunnerSessionExitedEvent(_sessionId, exited.ExitCode, exited.ExitReason, exited.LastSeq));
            }

            _exited.TrySetResult();
            // Fate is recorded — ack so the host deletes its manifest and exits. Run outside the
            // client's read loop (this handler IS the read loop).
            _ = Task.Run(ShutdownHostAsync);
        }

        private void HandleDisconnected(Exception? failure)
        {
            if (HasExited)
                return;

            // The host outlives us by design; a dropped pipe on a running session means the runner
            // is shutting down (adoption reconnects on next start) or the host died (the liveness
            // sweep will mark the vanished child). Nothing to do here but record it.
            _logger.LogWarning(
                failure,
                "pty-host pipe for running session {SessionId} disconnected (host pid {HostPid})",
                _sessionId, _hostPid);
        }

        private async Task ShutdownHostAsync()
        {
            var client = _client;
            if (client is null)
                return;

            try
            {
                await client.ShutdownAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Shutdown ack to pty-host for session {SessionId} failed (host likely already gone)",
                    _sessionId);
            }
        }

        private void KillHostBestEffort()
        {
            try
            {
                System.Diagnostics.Process.GetProcessById(_hostPid).Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
        }

        private PtyHostClient RequireClient() =>
            _client ?? throw new InvalidOperationException("Session has no live pty-host connection.");

        private string ReadAnsiLog()
        {
            if (_ansiLogPath is null || !File.Exists(_ansiLogPath))
                return "";

            // The host appends concurrently; open shared so reads never fail or block it.
            using var stream = new FileStream(
                _ansiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
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

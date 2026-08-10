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
    // One transcript, one session (CARD-0006 rule C1). Process-wide because the runner process is
    // the only thing that knows which sessions are live.
    private readonly TranscriptClaimRegistry _transcriptClaims = new();
    private readonly SessionRunnerSettings _settings;
    private readonly ShadowCopyStore _shadowStore;
    private readonly PtyHostLauncher _launcher;
    private readonly ILogger<SessionRunnerRuntime> _logger;

    public SessionRunnerRuntime(
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerRuntime> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _shadowStore = new ShadowCopyStore(_settings.PtyHostBinDir);
        _launcher = new PtyHostLauncher(_shadowStore, _settings.ResolvedPtyHostSourceDir);
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

        var session = new RunnerSession(request.SessionId, _settings, _events, _logger, _transcriptClaims);
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

    /// <param name="exitReasonOverride">When set, replaces the host's KilledByRequest exit reason
    /// so the server can distinguish WHY the runner killed the session (e.g. the CPU spin
    /// watchdog's <c>CpuSpinKilled</c>). A natural exit that wins the race keeps its own reason.</param>
    public async Task<RunnerSessionDto> KillAsync(
        Guid sessionId, TimeSpan timeout, CancellationToken ct, string? exitReasonOverride = null)
    {
        var session = GetSession(sessionId);
        await session.KillAsync(timeout, ct, exitReasonOverride);
        return session.ToDto();
    }

    public ChannelReader<RunnerServerSentEvent> Subscribe(CancellationToken ct) => _events.Subscribe(ct);

    /// <summary>Transcript ownership, rule C1 (see <see cref="TranscriptClaimRegistry"/>). Test surface.</summary>
    internal TranscriptClaimRegistry TranscriptClaims => _transcriptClaims;

    /// <summary>
    /// Kills every live session (and thereby its host, via the exit-&gt;Shutdown ack). The
    /// scorched-earth path behind <c>restart-session-runner.ps1 -KillSessions</c> and
    /// <c>POST /sessions/kill-all</c> - the ONLY sanctioned way to take hosts down in bulk.
    /// </summary>
    public async Task<IReadOnlyList<RunnerSessionDto>> KillAllAsync(TimeSpan timeout, CancellationToken ct)
    {
        var killed = new List<RunnerSessionDto>();
        foreach (var (sessionId, session) in _sessions)
        {
            if (session.HasExited)
                continue;
            try
            {
                await session.KillAsync(timeout, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Kill-all failed for session {SessionId}", sessionId);
            }

            killed.Add(session.ToDto());
        }

        return killed;
    }

    /// <summary>
    /// Best-effort disk hygiene for pty-host state: prunes shadow-copy version dirs no live host
    /// runs from (oldest first) and host logs past the retention window. Never throws.
    /// </summary>
    public void CleanupPtyHostState()
    {
        try
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_settings.PtyHostBinDir))
                referenced.Add(_launcher.CurrentShadowDir);

            foreach (var process in System.Diagnostics.Process.GetProcessesByName("Antiphon.PtyHost"))
            {
                try
                {
                    if (Path.GetDirectoryName(process.MainModule?.FileName) is { } dir)
                        referenced.Add(dir);
                }
                catch
                {
                    // Access denied / exited mid-scan - a locked dir survives deletion anyway.
                }
                finally
                {
                    process.Dispose();
                }
            }

            var deleted = _shadowStore.CleanupUnreferenced(referenced);
            if (deleted > 0)
                _logger.LogInformation("Pruned {Count} unreferenced pty-host shadow-copy dir(s)", deleted);

            var cutoff = DateTime.UtcNow.AddDays(-14);
            if (Directory.Exists(_settings.PtyHostLogDir))
            {
                foreach (var log in Directory.EnumerateFiles(_settings.PtyHostLogDir, "*.log"))
                {
                    if (File.GetLastWriteTimeUtc(log) < cutoff)
                        TryDeleteFile(log);
                }
            }

            // Transcript sidecars, same window — but only for sessions this runner no longer knows
            // about, since a live session's sidecar is how the NEXT restart re-tails it.
            var sidecarDir = TranscriptSidecar.DirectoryFor(_settings.SessionLogPath);
            if (Directory.Exists(sidecarDir))
            {
                foreach (var sidecar in Directory.EnumerateFiles(sidecarDir, "*.json"))
                {
                    if (File.GetLastWriteTimeUtc(sidecar) >= cutoff)
                        continue;
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(sidecar), "N", out var id)
                        && _sessions.ContainsKey(id))
                    {
                        continue;
                    }

                    TryDeleteFile(sidecar);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "pty-host state cleanup pass failed");
        }
    }

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
        // Rebuild transcript claims BEFORE any session is adopted. This sweep already has to
        // complete before the HTTP API starts listening, so restoring here means a freshly launched
        // session can never race the restore and discover a file a surviving session still owns.
        RestoreTranscriptClaims();

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
                var session = new RunnerSession(manifest.SessionId, _settings, _events, _logger, _transcriptClaims);
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

    /// <summary>
    /// Re-asserts every transcript claim recorded in a sidecar. A claim that outlives its session
    /// is deliberate: a previous session's transcript must never become adoptable by a new one, and
    /// a relaunch of the SAME session id (which is what <c>--resume</c> does) re-claims it as the
    /// same owner. Sidecars are pruned on the 14-day cleanup pass, so this cannot grow without bound.
    /// </summary>
    private void RestoreTranscriptClaims()
    {
        var restored = 0;
        foreach (var sidecar in TranscriptSidecar.LoadAll(_settings.SessionLogPath))
        {
            if (sidecar.TranscriptPath is { } path && _transcriptClaims.TryClaim(path, sidecar.SessionId))
                restored++;
        }

        if (restored > 0)
            _logger.LogInformation("Restored {Count} transcript claim(s) from sidecars", restored);
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
        // Completes true once the pty-host pipe is connected and the child launched; false once
        // the session is dead (failed start, exit, vanish, dispose). Input that arrives during
        // the cold-start window waits on this instead of failing (live miss 2026-08-09: the boot
        // prompt landed ~1s before the host process existed and was lost as an unhandled 500).
        private readonly TaskCompletionSource<bool> _clientReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TranscriptClaimRegistry? _transcriptClaims;
        // Everything typed into this session, normalized and bounded — the evidence rule C4 needs
        // to prove a candidate transcript is ours (CARD-0006).
        private readonly SessionInputLog _inputLog = new();

        private PtyHostClient? _client;
        private int _hostPid;
        private int? _childPid;
        private DateTime _startedAt;
        private TerminalScreen? _screen;
        private string? _ansiLogPath;
        private TranscriptTailer? _tailer;
        private TranscriptSidecar? _sidecar;
        private long _lastSequence;
        private string _status = "Starting";
        private int? _exitCode;
        private string _exitReason = PtyExitReason.Unknown.ToString();
        private string? _exitReasonOverride;
        private bool _adopted;

        public RunnerSession(
            Guid sessionId,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger,
            TranscriptClaimRegistry? transcriptClaims = null)
        {
            _sessionId = sessionId;
            _settings = settings;
            _events = events;
            _logger = logger;
            _transcriptClaims = transcriptClaims;
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
                _clientReady.TrySetResult(true);

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
                    // The sidecar is written BEFORE the tailer runs, so even a session that never
                    // binds a transcript leaves behind the facts (cwd, agent name, child start) a
                    // restart needs to judge candidates.
                    var agentName = FindArgValue(request.Args, "--name");
                    var resumeLaunch = IsResumeLaunch(request.Args);
                    SaveSidecar(new TranscriptSidecar
                    {
                        SessionId = _sessionId,
                        Cwd = request.Cwd,
                        AgentName = agentName,
                        ChildStartUtc = _startedAt,
                        ResumeLaunch = resumeLaunch,
                        TranscriptPath = null,
                        How = null,
                    });

                    _tailer = new TranscriptTailer(
                        _sessionId, request.Cwd, _events, _logger,
                        claims: _transcriptClaims,
                        inputLog: _inputLog,
                        childStartUtc: _startedAt,
                        agentName: agentName,
                        resumeLaunch: resumeLaunch,
                        onBound: RecordTranscriptBinding);
                    _tailer.Start();
                }
            }
            catch
            {
                // Never leave an orphaned empty host behind a failed start.
                _clientReady.TrySetResult(false);
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

            _clientReady.TrySetResult(true);
            _events.Publish(
                SessionRunnerEventNames.SessionAdopted,
                new RunnerSessionAdoptedEvent(_sessionId, _childPid, _startedAt, status.LastSeq));

            if (manifest.TranscriptEnabled)
            {
                // Re-tail the file we already knew about instead of re-running discovery: after a
                // restart the input log is empty, so nothing could prove ownership of a candidate,
                // and the heuristic that used to fill that gap is what bound an agent to the
                // operator's own conversation (CARD-0006).
                var sidecar = TranscriptSidecar.TryLoad(
                    TranscriptSidecar.PathFor(_settings.SessionLogPath, _sessionId));
                var cwd = manifest.Cwd ?? sidecar?.Cwd ?? "";
                // A session that predates sidecars has none to load; seed one from the manifest so
                // this restart is the last one that has to fall back to the migration shim.
                _sidecar = sidecar ?? new TranscriptSidecar
                {
                    SessionId = _sessionId,
                    Cwd = cwd,
                    ChildStartUtc = manifest.ChildStartTimeUtc,
                };

                _tailer = new TranscriptTailer(
                    _sessionId, cwd, _events, _logger,
                    claims: _transcriptClaims,
                    inputLog: _inputLog,
                    childStartUtc: manifest.ChildStartTimeUtc ?? sidecar?.ChildStartUtc,
                    agentName: sidecar?.AgentName,
                    resumeLaunch: sidecar?.ResumeLaunch ?? false,
                    knownTranscriptPath: sidecar?.TranscriptPath,
                    restartAdopt: true,
                    onBound: RecordTranscriptBinding);
                _tailer.Start();
            }

            return true;
        }

        /// <summary>Persists the transcript binding so the next runner re-tails it without guessing.</summary>
        private void RecordTranscriptBinding(string transcriptPath, string how)
        {
            var current = _sidecar ?? new TranscriptSidecar { SessionId = _sessionId, ChildStartUtc = _startedAt };
            SaveSidecar(current with { TranscriptPath = transcriptPath, How = how });
        }

        private void SaveSidecar(TranscriptSidecar sidecar)
        {
            _sidecar = sidecar with { UpdatedAtUtc = DateTime.UtcNow };
            try
            {
                _sidecar.SaveAtomic(TranscriptSidecar.PathFor(_settings.SessionLogPath, _sessionId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: losing the sidecar costs this session its no-guess restart path, not
                // its transcript. It is never a reason to fail a launch.
                _logger.LogWarning(ex, "Could not write the transcript sidecar for session {SessionId}", _sessionId);
            }
        }

        private static string? FindArgValue(IReadOnlyList<string> args, string name)
        {
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i] == name && i + 1 < args.Count)
                    return args[i + 1];
                if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                    return args[i][(name.Length + 1)..];
            }

            return null;
        }

        // --resume/--continue replay a conversation whose records legitimately predate this launch,
        // which is exactly what rule C3 would otherwise reject.
        private static bool IsResumeLaunch(IReadOnlyList<string> args) =>
            args.Any(a =>
                a is "--resume" or "-r" or "--continue" or "-c"
                || a.StartsWith("--resume=", StringComparison.Ordinal));

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
            session._clientReady.TrySetResult(false);
            session._exited.TrySetResult();

            events.Publish(
                SessionRunnerEventNames.SessionExited,
                new RunnerSessionExitedEvent(
                    manifest.SessionId, session._exitCode, session._exitReason, LastSequence: 0));
            return session;
        }

        private void RebuildInterpretationFromAnsiLog(long lastSeq)
        {
            // ReadAnsiLog already bounds itself to ReplayBufferMaxChars.
            var replay = ReadAnsiLog();

            lock (_gate)
            {
                _liveBuffer.Clear();
                _liveBuffer.Append(replay);
                _screen?.Feed(replay);
                _lastSequence = Math.Max(_lastSequence, lastSeq);
            }
        }

        /// <summary>
        /// Evict from the front so the runner's mirror of the session stays bounded — the pty-host
        /// bounds its own ring the same way and to the same cap (see <c>HostSession</c>). Without
        /// this the mirror grew for the life of the session, and every snapshot payload built from
        /// it grew with it. Trimming only once the buffer is over twice the cap amortises the
        /// memmove to roughly one per <c>cap</c> chars appended instead of one per chunk.
        /// Caller must hold <see cref="_gate"/>.
        /// </summary>
        private void TrimLiveBuffer()
        {
            var cap = Math.Max(1, _settings.ReplayBufferMaxChars);
            if (_liveBuffer.Length > cap * 2L)
                _liveBuffer.Remove(0, _liveBuffer.Length - cap);
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

        public async Task WriteAsync(string input, CancellationToken ct)
        {
            var client = await AwaitClientAsync(ct);
            // Recorded BEFORE the write: Claude cannot persist a prompt we have not sent yet, so
            // the input log is always ahead of the transcript record that rule C4 matches it to.
            _inputLog.Append(input);
            await client.InputAsync(input, ct);
        }

        public async Task ClearLiveBufferAsync(CancellationToken ct)
        {
            lock (_gate)
                _liveBuffer.Clear();
            if (_client is { } client)
                await client.ClearLiveBufferAsync(ct);
        }

        public async Task ResizeAsync(int cols, int rows, CancellationToken ct)
        {
            var client = await AwaitClientAsync(ct);
            await client.ResizeAsync(cols, rows, ct);
        }

        public async Task KillAsync(TimeSpan timeout, CancellationToken ct, string? exitReasonOverride = null)
        {
            if (HasExited || _client is not { } client)
                return;

            if (exitReasonOverride is not null)
            {
                lock (_gate)
                    _exitReasonOverride = exitReasonOverride;
            }

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
            _clientReady.TrySetResult(false);
            _exited.TrySetResult();
            _tailer?.NotifyChildExited();

            // The session is declared dead; the host (if any survives) has no further purpose.
            _ = Task.Run(ShutdownHostAsync);
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            _clientReady.TrySetResult(false);
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
                TrimLiveBuffer();
                _screen?.Feed(chunk);
            }

            _events.Publish(
                SessionRunnerEventNames.SessionOutput,
                new RunnerOutputEvent(_sessionId, seq, chunk));
        }

        private void HandleExited(ExitedMessage exited)
        {
            bool transitioned;
            string exitReason;
            lock (_gate)
            {
                transitioned = _status != "Exited";
                // The override only applies when OUR kill is what ended the child (the host says
                // KilledByRequest) — a natural exit that races the kill keeps its real reason.
                exitReason = _exitReasonOverride is { } requested
                    && exited.ExitReason == PtyExitReason.KilledByRequest.ToString()
                        ? requested
                        : exited.ExitReason;
                if (transitioned)
                {
                    _status = "Exited";
                    _exitCode = exited.ExitCode;
                    _exitReason = exitReason;
                    _lastSequence = Math.Max(_lastSequence, exited.LastSeq);
                }
            }

            if (transitioned)
            {
                _events.Publish(
                    SessionRunnerEventNames.SessionExited,
                    new RunnerSessionExitedEvent(_sessionId, exited.ExitCode, exitReason, exited.LastSeq));
            }

            _clientReady.TrySetResult(false);
            _exited.TrySetResult();
            // A dead child writes no more transcript: stop hunting for one (and say so if input was
            // delivered and nothing ever bound).
            _tailer?.NotifyChildExited();
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

        /// <summary>
        /// The connected client, waiting out the cold-start window when necessary. On a cold
        /// launch the pty-host pipe takes ~a second to appear AFTER the session is registered, so
        /// input that raced the launch used to die as "Session has no live pty-host connection"
        /// and the boot prompt was silently lost (CARD-0018). Bounded by the same budget the
        /// launch itself gets; a session that dies first fails fast with the reason.
        /// </summary>
        private async Task<PtyHostClient> AwaitClientAsync(CancellationToken ct)
        {
            if (_client is { } live)
                return live;

            var timeout = TimeSpan.FromSeconds(
                _settings.PtyHostLaunchTimeoutSec + _settings.PtyHostConnectTimeoutSec + 5);
            var completed = await Task.WhenAny(_clientReady.Task, Task.Delay(timeout, ct));
            ct.ThrowIfCancellationRequested();

            if (completed != _clientReady.Task)
            {
                throw new InvalidOperationException(
                    $"Session has no live pty-host connection after waiting {timeout.TotalSeconds:0}s for the host to start.");
            }

            if (!await _clientReady.Task || _client is not { } client)
                throw new InvalidOperationException("Session ended before its pty-host connection was ready.");

            return client;
        }

        /// <summary>
        /// The tail of the ANSI log, bounded to <see cref="SessionRunnerSettings.ReplayBufferMaxChars"/>.
        /// Never reads the whole file: these logs reach tens of MB, and both callers (replay-on-adopt
        /// and the /buffer endpoint the server polls every 50ms) only ever wanted the tail. Reading
        /// the lot each time churned the LOH hard enough to strand multi-GB ArrayPool buckets for the
        /// life of the process.
        /// </summary>
        private string ReadAnsiLog()
        {
            if (_ansiLogPath is null || !File.Exists(_ansiLogPath))
                return "";

            var cap = Math.Max(1, _settings.ReplayBufferMaxChars);

            // The host appends concurrently; open shared so reads never fail or block it.
            using var stream = new FileStream(
                _ansiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // UTF-8 is at most 4 bytes/char, so this window always yields at least `cap` chars.
            var window = cap * 4L;
            if (stream.Length > window)
            {
                stream.Seek(-window, SeekOrigin.End);

                // That lands mid-file and possibly mid-character. Continuation bytes are 10xxxxxx:
                // skip them so the decoder starts on a real character boundary.
                for (var i = 0; i < 3; i++)
                {
                    var b = stream.ReadByte();
                    if (b < 0)
                        break;
                    if ((b & 0xC0) != 0x80)
                    {
                        stream.Seek(-1, SeekOrigin.Current);
                        break;
                    }
                }
            }

            // The host writes UTF-8 with no BOM (File.AppendAllText), and we may be mid-file, so
            // don't let a stray byte triple be mistaken for one.
            using var reader = new StreamReader(
                stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false);
            var text = reader.ReadToEnd();

            return text.Length > cap ? text[^cap..] : text;
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

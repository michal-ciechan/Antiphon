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
    // The enum is the runner's actual dispatch surface. /capabilities derives its list from this
    // rather than a separately-maintained contract list, so a new switch arm cannot be omitted
    // from the advertised answer (CARD-0112 S1).
    internal enum TranscriptTailerKind { Claude, Grok, Codex }

    public static IReadOnlyList<string> SupportedTranscriptFormats { get; } =
        Enum.GetValues<TranscriptTailerKind>().Select(FormatFor).ToArray();

    private readonly ConcurrentDictionary<Guid, RunnerSession> _sessions = new();
    private readonly SessionRunnerEventHub _events = new();
    // One transcript, one session (CARD-0006 rule C1). Process-wide because the runner process is
    // the only thing that knows which sessions are live.
    private readonly TranscriptClaimRegistry _transcriptClaims = new();
    private readonly SessionRunnerSettings _settings;
    private readonly ShadowCopyStore _shadowStore;
    private readonly PtyHostLauncher _launcher;
    private readonly HerdrClient? _herdrClient;
    private readonly ILogger<SessionRunnerRuntime> _logger;

    /// <summary>
    /// CARD-0162: fired when the set of live herdr panes changes (launch / adopt / exit) so the
    /// event pump can recycle its subscription.
    /// </summary>
    public event Action? PaneSetChanged;

    public SessionRunnerRuntime(
        IOptions<SessionRunnerSettings> settings,
        ILogger<SessionRunnerRuntime> logger,
        HerdrClient? herdrClient = null)
    {
        _settings = settings.Value;
        _logger = logger;
        _herdrClient = herdrClient;
        _shadowStore = new ShadowCopyStore(_settings.PtyHostBinDir);
        _launcher = new PtyHostLauncher(_shadowStore, _settings.ResolvedPtyHostSourceDir);
    }

    internal void NotifyPaneSetChanged() => PaneSetChanged?.Invoke();

    /// <summary>
    /// CARD-0162: live herdr sessions with a known pane id (pump subscription + verification).
    /// </summary>
    internal IReadOnlyList<LiveHerdrPane> LiveHerdrPanes()
    {
        var result = new List<LiveHerdrPane>();
        foreach (var (id, session) in _sessions)
        {
            if (session.HasExited) continue;
            if (session.HerdrPaneId is not { } paneId) continue;
            result.Add(new LiveHerdrPane(id, paneId, session));
        }

        return result;
    }

    internal readonly record struct LiveHerdrPane(Guid SessionId, string PaneId, RunnerSession Session);

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
        if (request.TranscriptFormat is { } format
            && !TryResolveTranscriptTailer(format, out _))
        {
            throw new UnsupportedTranscriptFormatException(format, SupportedTranscriptFormats);
        }

        // CARD-0160: Backend validation. Null = pty-host (pre-herdr meaning). Unknown → throw.
        var backend = request.Backend;
        if (backend is not null
            && !string.Equals(backend, SessionBackends.PtyHost, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(backend, SessionBackends.Herdr, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unsupported session backend '{backend}'. Supported: {SessionBackends.PtyHost}, {SessionBackends.Herdr}.",
                nameof(request));
        }

        var useHerdr = string.Equals(backend, SessionBackends.Herdr, StringComparison.OrdinalIgnoreCase);
        if (useHerdr && request.Herdr is null)
            throw new ArgumentException("Herdr launch requires HerdrLaunchOptions.", nameof(request));
        if (useHerdr && _herdrClient is null)
            throw new HerdrBackendUnavailableException(
                "Herdr backend is not available in this runner process (HerdrClient was not registered).");

        var session = new RunnerSession(request.SessionId, _settings, _events, _logger, _transcriptClaims);
        if (!_sessions.TryAdd(request.SessionId, session))
        {
            // A session id can be relaunched once its process has exited (claude --resume reuses
            // the original id); only a live session blocks the id.
            if (_sessions.TryGetValue(request.SessionId, out var existing)
                && existing.HasExited
                && _sessions.TryUpdate(request.SessionId, session, existing))
            {
                // CARD-0050: the pipe name derives from the session id, so a relaunch races the
                // PREVIOUS host's teardown — its Shutdown ack is fire-and-forget (HandleExited),
                // and until that host exits it still owns a pipe server instance with the exact
                // name the new host will claim. Under load the new client's connect reached the
                // dying host, which correctly answered "alreadyLaunched: Session is Exited" and
                // failed the relaunch. The child is already exited here, so forcing the old host
                // out forfeits nothing a pty-host exists to protect.
                await existing.EnsureExitedHostGoneAsync(TimeSpan.FromSeconds(5), ct);
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
            if (useHerdr)
            {
                await session.StartHerdrAsync(
                    request,
                    _herdrClient!,
                    () => CollectLiveAntiphonPanes(request.Herdr!.WorkspaceKey),
                    () => NotifyPaneSetChanged(),
                    ct);
                NotifyPaneSetChanged();
            }
            else
            {
                await session.StartAsync(request, _launcher, ct);
            }

            return session.ToDto();
        }
        catch
        {
            _sessions.TryRemove(request.SessionId, out _);
            // Kill then dispose — DisposeAsync is detach-not-kill (pty-host split). Same shape
            // as AgentSessionService.KillAndDisposeAsync (CARD-0056 D1, CARD-0086).
            session.TearDownFailedLaunch();
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>Live Antiphon herdr panes for the allocator (sidecar + still in this runner).</summary>
    private IReadOnlyList<HerdrPaneAllocator.LivePane> CollectLiveAntiphonPanes(string workspaceKey)
    {
        var result = new List<HerdrPaneAllocator.LivePane>();
        foreach (var sidecar in HerdrPaneSidecar.LoadAll(_settings.SessionLogPath))
        {
            if (!string.Equals(sidecar.WorkspaceKey, workspaceKey, StringComparison.Ordinal))
                continue;
            if (!_sessions.TryGetValue(sidecar.SessionId, out var session) || session.HasExited)
                continue;
            // TabNumber unknown from sidecar alone — use 0 and let allocator order by TabId as tiebreak.
            // Callers that have tab.get can refine; for gap refill within one tab, number equality is fine.
            result.Add(new HerdrPaneAllocator.LivePane(
                sidecar.SessionId, sidecar.TabId, sidecar.PaneId, TabNumber: 0));
        }

        return result;
    }

    internal static bool TryResolveTranscriptTailer(string format, out TranscriptTailerKind tailer)
    {
        foreach (var candidate in Enum.GetValues<TranscriptTailerKind>())
        {
            if (string.Equals(FormatFor(candidate), format, StringComparison.OrdinalIgnoreCase))
            {
                tailer = candidate;
                return true;
            }
        }

        tailer = default;
        return false;
    }

    private static string FormatFor(TranscriptTailerKind tailer) => tailer switch
    {
        TranscriptTailerKind.Claude => TranscriptFormats.Claude,
        TranscriptTailerKind.Grok => TranscriptFormats.Grok,
        TranscriptTailerKind.Codex => TranscriptFormats.Codex,
        _ => throw new ArgumentOutOfRangeException(nameof(tailer), tailer, null),
    };

    public IReadOnlyList<RunnerSessionDto> List() =>
        _sessions.Values.Select(session => session.ToDto()).OrderBy(session => session.StartedAt).ToList();

    public RunnerSessionDto Get(Guid sessionId) => GetSession(sessionId).ToDto();

    /// <summary>
    /// CARD-0161: for herdr sessions, refresh LastSequence + AgentStatus via one pane.get before
    /// answering. List() stays cheap (no herdr calls).
    /// </summary>
    public async Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        var session = GetSession(sessionId);
        await session.RefreshHerdrSurfaceAsync(ct);
        return session.ToDto();
    }

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

        // CARD-0160: herdr adoption arm AFTER claims. Sidecar present + pane/pid/read evidence →
        // re-adopt; restored-but-empty or unknown pane → Exited(HerdrRestartPresumedDead);
        // herdr unreachable → no verdict (alert + retry via liveness).
        if (_herdrClient is not null)
            await AdoptHerdrSessionsAsync(ct);

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
    /// CARD-0160 §6A: adopt or mark-exited each <see cref="HerdrPaneSidecar"/>. Evidence bar is
    /// CARD-0056 transposed: named child pid present in pane.process_info AND pane.read answers.
    /// Restored-but-empty panes (herdr restart) are positively DEAD — never false-adopted.
    /// </summary>
    private async Task AdoptHerdrSessionsAsync(CancellationToken ct)
    {
        foreach (var sidecar in HerdrPaneSidecar.LoadAll(_settings.SessionLogPath))
        {
            ct.ThrowIfCancellationRequested();
            if (_sessions.ContainsKey(sidecar.SessionId))
                continue;

            try
            {
                await _herdrClient!.ConnectAndValidateAsync(ct);
                HerdrPaneInfo pane;
                try
                {
                    pane = await _herdrClient.PaneGetAsync(sidecar.PaneId, ct);
                }
                catch (HerdrApiException)
                {
                    // Unknown pane → Exited(HerdrRestartPresumedDead).
                    RegisterHerdrExited(sidecar, "HerdrRestartPresumedDead");
                    continue;
                }

                var proc = await _herdrClient.PaneProcessInfoAsync(sidecar.PaneId, ct);
                var childPresent = sidecar.ChildPid is int child
                    && proc.ForegroundProcesses?.Any(p => p.Pid == child) == true;
                if (!childPresent)
                {
                    // Restored-but-empty trap: pane exists, our child pid is gone.
                    RegisterHerdrExited(sidecar, "HerdrRestartPresumedDead");
                    continue;
                }

                // Positive evidence: pid present — also require pane.read to answer.
                _ = await _herdrClient.PaneReadAsync(sidecar.PaneId, "visible", stripAnsi: true, lines: 1, ct);

                var session = new RunnerSession(sidecar.SessionId, _settings, _events, _logger, _transcriptClaims);
                await session.AdoptHerdrAsync(sidecar, _herdrClient, () => NotifyPaneSetChanged(), ct);
                _sessions.TryAdd(sidecar.SessionId, session);
                NotifyPaneSetChanged();
                _logger.LogInformation(
                    "Adopted herdr pane {PaneId} for session {SessionId} (child pid {Pid})",
                    sidecar.PaneId, sidecar.SessionId, sidecar.ChildPid);
            }
            catch (HerdrBackendUnavailableException ex)
            {
                // Unreachable → no verdict. Leave sidecar; liveness/retry will try again.
                _logger.LogWarning(ex,
                    "Herdr unreachable while adopting session {SessionId}; leaving unadopted",
                    sidecar.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Herdr adoption failed for session {SessionId}; registering Exited",
                    sidecar.SessionId);
                RegisterHerdrExited(sidecar, "HerdrRestartPresumedDead");
            }
        }
    }

    private void RegisterHerdrExited(HerdrPaneSidecar sidecar, string reason)
    {
        var exited = RunnerSession.CreateAdoptedHerdrExited(sidecar, _settings, _events, _logger, reason);
        _sessions.TryAdd(sidecar.SessionId, exited);
        HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, sidecar.SessionId);
        _logger.LogWarning(
            "Herdr pane {PaneId} for session {SessionId} registered as Exited ({Reason})",
            sidecar.PaneId, sidecar.SessionId, reason);
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

    /// <summary>Per-session state. Internal so <see cref="HerdrEventPumpService"/> can verify/apply status.</summary>
    internal sealed class RunnerSession : IAsyncDisposable
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
        private ISessionChild? _herdrChild;
        private string? _herdrAgentStatus;
        private DateTime? _herdrAgentStatusSinceUtc;
        private Action? _onHerdrPaneSetChanged;
        private int _hostPid;
        private int? _childPid;
        private DateTime _startedAt;
        private TerminalScreen? _screen;
        private string? _ansiLogPath;
        private ITranscriptTailer? _tailer;
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

        /// <summary>CARD-0162: pane id when this session is on the herdr lane.</summary>
        internal string? HerdrPaneId => (_herdrChild as HerdrPaneChild)?.PaneId;

        /// <summary>CARD-0161: fold pane.get revision into LastSequence and capture AgentStatus.</summary>
        public async Task RefreshHerdrSurfaceAsync(CancellationToken ct)
        {
            if (_herdrChild is not HerdrPaneChild herdr)
                return;

            try
            {
                var (revision, status) = await herdr.RefreshStatusAsync(ct);
                lock (_gate)
                {
                    _lastSequence = Math.Max(_lastSequence, revision);
                }

                if (status is not null)
                    ApplyHerdrAgentStatus(status, DateTime.UtcNow, publishEvent: false);
            }
            catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException)
            {
                _logger.LogDebug(ex, "Herdr pane.get refresh failed for session {SessionId}", _sessionId);
            }
        }

        /// <summary>
        /// CARD-0162: update the herdr status cache. <paramref name="publishEvent"/> is true for
        /// pump-driven changes (SSE to server); GET refresh updates the cache silently.
        /// </summary>
        internal void ApplyHerdrAgentStatus(string status, DateTime observedAtUtc, bool publishEvent = true)
        {
            string? previous;
            lock (_gate)
            {
                previous = _herdrAgentStatus;
                if (string.Equals(previous, status, StringComparison.Ordinal))
                {
                    // Same value — since does not move (hysteresis).
                    return;
                }

                _herdrAgentStatus = status;
                _herdrAgentStatusSinceUtc = observedAtUtc;
            }

            if (publishEvent)
            {
                _events.Publish(
                    SessionRunnerEventNames.SessionAgentStatus,
                    new RunnerAgentStatusEvent(_sessionId, status, previous, observedAtUtc));
            }
        }

        /// <summary>
        /// CARD-0162: §6A evidence bar as a runtime check. Events are triggers only — alive means
        /// pane.get answers AND ChildPid is in process_info. Unreachable → no verdict (true).
        /// </summary>
        internal async Task<bool> VerifyHerdrLivenessAsync(HerdrClient client, CancellationToken ct)
        {
            if (_herdrChild is not HerdrPaneChild herdr || herdr.PaneId is null || herdr.Sidecar is null)
                return true;

            var sidecar = herdr.Sidecar;
            try
            {
                try
                {
                    _ = await client.PaneGetAsync(sidecar.PaneId, ct);
                }
                catch (HerdrApiException)
                {
                    herdr.RaiseVerifiedClosed("HerdrPaneClosed");
                    return false;
                }

                if (sidecar.ChildPid is int childPid)
                {
                    var proc = await client.PaneProcessInfoAsync(sidecar.PaneId, ct);
                    var childPresent = proc.ForegroundProcesses?.Any(p => p.Pid == childPid) == true;
                    if (!childPresent)
                    {
                        herdr.RaiseVerifiedClosed("HerdrPaneClosed");
                        return false;
                    }
                }

                // ChildPid null: pane existence alone (weaker bar, stated honestly).
                return true;
            }
            catch (HerdrBackendUnavailableException)
            {
                // Unreachable is never evidence of death.
                return true;
            }
        }

        /// <summary>CARD-0160 herdr lane — shares transcript/input-log machinery with the pty path.</summary>
        public async Task StartHerdrAsync(
            RunnerLaunchRequest request,
            HerdrClient herdrClient,
            Func<IReadOnlyList<HerdrPaneAllocator.LivePane>> liveAntiphonPanes,
            Action onPaneSetChanged,
            CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SessionLogPath);
            _ansiLogPath = Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(request.Cols > 0 ? request.Cols : 120, request.Rows > 0 ? request.Rows : 30);
            _onHerdrPaneSetChanged = onPaneSetChanged;

            try
            {
                _herdrChild = new HerdrPaneChild(herdrClient, _settings, _logger, liveAntiphonPanes);
                _herdrChild.Exited += exit =>
                {
                    lock (_gate)
                    {
                        if (_status == "Exited") return;
                        _status = "Exited";
                        _exitCode = exit.ExitCode;
                        _exitReason = exit.Reason;
                    }

                    _clientReady.TrySetResult(false);
                    _exited.TrySetResult();
                    _events.Publish(
                        SessionRunnerEventNames.SessionExited,
                        new RunnerSessionExitedEvent(_sessionId, exit.ExitCode, exit.Reason, LastSequence: 0));
                    _onHerdrPaneSetChanged?.Invoke();
                };

                var started = await _herdrChild.LaunchAsync(request, ct);
                _childPid = started.ChildPid;
                _startedAt = started.ChildStartUtc;
                lock (_gate)
                    _status = "Running";
                _clientReady.TrySetResult(true);

                _events.Publish(
                    SessionRunnerEventNames.SessionStarted,
                    new RunnerSessionStartedEvent(_sessionId, _childPid, _startedAt));

                // Same transcript binding as pty — S1 proved herdr-launched Claude writes cwd-keyed JSONL.
                StartTranscriptTailer(request, started.ChildStartUtc);
            }
            catch
            {
                _clientReady.TrySetResult(false);
                if (_herdrChild is not null)
                {
                    try { await _herdrChild.KillAsync(CancellationToken.None); }
                    catch { /* tear-down must not replace the launch exception */ }
                    await _herdrChild.DisposeAsync();
                    _herdrChild = null;
                }

                throw;
            }
        }

        private void StartTranscriptTailer(RunnerLaunchRequest request, DateTime childStartUtc)
        {
            if (!request.TranscriptEnabled)
                return;

            // Herdr S2 only spikes Claude (plan §7). Other formats stay on the pty-host lane.
            var agentName = FindArgValue(request.Args, "--name");
            var resumeLaunch = IsResumeLaunch(request.Args);
            SaveSidecar(new TranscriptSidecar
            {
                SessionId = _sessionId,
                Cwd = request.Cwd,
                AgentName = agentName,
                ChildStartUtc = childStartUtc,
                ResumeLaunch = resumeLaunch,
                TranscriptPath = null,
                How = null,
                Format = TranscriptFormats.Claude,
            });

            _tailer = new TranscriptTailer(
                _sessionId, request.Cwd, _events, _logger,
                claims: _transcriptClaims,
                inputLog: _inputLog,
                childStartUtc: childStartUtc,
                agentName: agentName,
                resumeLaunch: resumeLaunch,
                onBound: RecordTranscriptBinding);
            _tailer.Start();
        }

        public async Task StartAsync(RunnerLaunchRequest request, PtyHostLauncher launcher, CancellationToken ct)
        {
            Directory.CreateDirectory(_settings.SessionLogPath);
            Directory.CreateDirectory(_settings.PtyHostLogDir);
            _ansiLogPath = Path.Combine(_settings.SessionLogPath, $"{_sessionId:N}.ansi.log");
            _screen = new TerminalScreen(request.Cols, request.Rows);

            try
            {
                _hostPid = await launcher.LaunchDetachedAsync(
                    _sessionId,
                    _settings.PtyHostManifestDir,
                    hostLogFile: Path.Combine(_settings.PtyHostLogDir, $"{_sessionId:N}.log"),
                    launchTimeout: TimeSpan.FromSeconds(_settings.PtyHostLaunchTimeoutSec),
                    lingerTtl: TimeSpan.FromHours(_settings.PtyHostLingerHours),
                    ringCapChars: Math.Max(1, _settings.ReplayBufferMaxChars),
                    // CARD-0045: state the backend on the host's command line instead of relying on it
                    // inheriting our environment block. Production is unchanged — the daemon exports the
                    // same SessionRunner:PtyBackend value into ANTIPHON_PTY_BACKEND at startup, so the
                    // host now hears the same answer twice. What it BUYS is the host-mediated tests: a
                    // caller that builds its own runtime (DirectSessionRunnerClient) could not reach
                    // PtyAgentRunner's per-instance override at all, three processes down, and so ran on
                    // whatever the test process had inherited.
                    ptyBackend: _settings.PtyBackend,
                    ct: ct);

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

                var transcriptTailer = request.TranscriptFormat is { } requestedFormat
                    ? TryResolveTranscriptTailer(requestedFormat, out var resolvedTailer)
                        ? resolvedTailer
                        : throw new UnsupportedTranscriptFormatException(requestedFormat, SupportedTranscriptFormats)
                    : TranscriptTailerKind.Claude;
                if (request.TranscriptEnabled && transcriptTailer == TranscriptTailerKind.Grok)
                {
                    // Grok's transcript path is DETERMINISTIC (we pass --session-id and grok
                    // honours it — measured 1.0.5, CARD-0080 S1), so the sidecar records the bound
                    // path up front and none of the Claude discovery/claim machinery runs. A
                    // restart re-tails this exact file via the sidecar's Format + TranscriptPath.
                    var updatesPath = GrokTranscriptTailer.ResolveUpdatesPath(
                        request.Env, request.Cwd, _sessionId);
                    SaveSidecar(new TranscriptSidecar
                    {
                        SessionId = _sessionId,
                        Cwd = request.Cwd,
                        ChildStartUtc = _startedAt,
                        ResumeLaunch = IsResumeLaunch(request.Args),
                        TranscriptPath = updatesPath,
                        How = TranscriptBindMethods.Deterministic,
                        Format = TranscriptFormats.Grok,
                    });

                    _tailer = new GrokTranscriptTailer(
                        _sessionId, updatesPath, _events, _logger, inputLog: _inputLog);
                    _tailer.Start();
                }
                else if (request.TranscriptEnabled && transcriptTailer == TranscriptTailerKind.Codex)
                {
                    // Codex's rollout path is NOT deterministic — there is no --session-id flag and
                    // the TUI never prints its id (CARD-0099 S1) — so this runs the same CARD-0006
                    // discovery Claude does, over CODEX_HOME/sessions instead. The sidecar records
                    // the facts a restart needs to judge candidates, and TranscriptPath is filled in
                    // by the onBound callback once a rollout is positively identified.
                    SaveSidecar(new TranscriptSidecar
                    {
                        SessionId = _sessionId,
                        Cwd = request.Cwd,
                        ChildStartUtc = _startedAt,
                        ResumeLaunch = IsCodexResumeLaunch(request.Args),
                        TranscriptPath = null,
                        How = null,
                        Format = TranscriptFormats.Codex,
                    });

                    _tailer = new CodexTranscriptTailer(
                        _sessionId, request.Cwd, _events, _logger,
                        claims: _transcriptClaims,
                        inputLog: _inputLog,
                        childStartUtc: _startedAt,
                        resumeLaunch: IsCodexResumeLaunch(request.Args),
                        sessionsRoot: CodexTranscriptTailer.ResolveSessionsRoot(request.Env),
                        onBound: RecordTranscriptBinding);
                    _tailer.Start();
                }
                else if (request.TranscriptEnabled)
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
                TearDownFailedLaunch();
                throw;
            }
        }

        /// <summary>
        /// CARD-0086: kill the host a failed <see cref="StartAsync"/> spawned. DisposeAsync is
        /// detach-not-kill (pty-host split); this is the runner analogue of
        /// <c>AgentSessionService.KillAndDisposeAsync</c>. Never throws — a kill failure must
        /// not replace the launch exception. Double-kill is harmless.
        /// </summary>
        internal void TearDownFailedLaunch()
        {
            if (_hostPid <= 0)
                return;

            try
            {
                KillHostIfStillOurs();
            }
            catch
            {
                // Already gone / pid reuse / access denied.
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
            var runnerVersion = RunnerBuildIdentity.Resolve().InformationalVersion;
            if (!string.Equals(_client.Hello.HostVersion, runnerVersion, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Adopted pty-host for session {SessionId} was built as {HostVersion}, while this session runner is {RunnerVersion}",
                    _sessionId, _client.Hello.HostVersion, runnerVersion);
            }
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

                // A Grok sidecar re-tails the same deterministic file — no discovery, no shim. The
                // sidecar always exists for a Grok session (written before the tailer at launch);
                // a missing TranscriptPath (hand-edited sidecar) recomputes it from cwd + id, with
                // this process's GROK_HOME standing in for the launch env the manifest never keeps.
                if (string.Equals(sidecar?.Format, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase))
                {
                    _sidecar = sidecar;
                    var updatesPath = sidecar!.TranscriptPath
                        ?? GrokTranscriptTailer.ResolveUpdatesPath(null, cwd, _sessionId);
                    _tailer = new GrokTranscriptTailer(
                        _sessionId, updatesPath, _events, _logger, inputLog: _inputLog);
                    _tailer.Start();
                    return true;
                }

                // A Codex sidecar re-tails the recorded rollout directly. If the session never
                // bound one before the restart, discovery runs again — and correctly finds nothing,
                // because the input log is empty after a restart so C4 cannot be satisfied until
                // new input arrives. That is the same conservative outcome the Claude path has:
                // running unbound is a fault to report, never a reason to relax the rules.
                if (string.Equals(sidecar?.Format, TranscriptFormats.Codex, StringComparison.OrdinalIgnoreCase))
                {
                    _sidecar = sidecar;
                    _tailer = new CodexTranscriptTailer(
                        _sessionId, cwd, _events, _logger,
                        claims: _transcriptClaims,
                        inputLog: _inputLog,
                        childStartUtc: manifest.ChildStartTimeUtc ?? sidecar!.ChildStartUtc,
                        resumeLaunch: sidecar!.ResumeLaunch,
                        knownTranscriptPath: sidecar.TranscriptPath,
                        onBound: RecordTranscriptBinding);
                    _tailer.Start();
                    return true;
                }
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
        /// <summary>
        /// Codex's own resume vocabulary: <c>codex resume</c> / <c>codex fork</c> (subcommands, not
        /// flags — <c>codex --help</c>, 0.147.0). Deliberately separate from
        /// <see cref="IsResumeLaunch"/>: Codex's <c>-c</c> is <c>--config</c>, not <c>--continue</c>,
        /// so reusing the Claude predicate would waive C3 on every configured launch.
        /// </summary>
        private static bool IsCodexResumeLaunch(IReadOnlyList<string> args) =>
            args.Any(a => a is "resume" or "fork");

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

        public static RunnerSession CreateAdoptedHerdrExited(
            HerdrPaneSidecar sidecar,
            SessionRunnerSettings settings,
            SessionRunnerEventHub events,
            ILogger logger,
            string reason)
        {
            var session = new RunnerSession(sidecar.SessionId, settings, events, logger)
            {
                _childPid = sidecar.ChildPid,
                _startedAt = sidecar.LaunchedAtUtc,
                _adopted = true,
                _status = "Exited",
                _exitCode = null,
                _exitReason = reason,
            };
            session._clientReady.TrySetResult(false);
            session._exited.TrySetResult();
            events.Publish(
                SessionRunnerEventNames.SessionExited,
                new RunnerSessionExitedEvent(sidecar.SessionId, null, reason, LastSequence: 0));
            return session;
        }

        /// <summary>Re-attach a surviving herdr pane after a runner restart (CARD-0160 §6A positive arm).</summary>
        public async Task AdoptHerdrAsync(
            HerdrPaneSidecar sidecar,
            HerdrClient client,
            Action onPaneSetChanged,
            CancellationToken ct)
        {
            _adopted = true;
            _childPid = sidecar.ChildPid;
            _startedAt = sidecar.LaunchedAtUtc;
            _onHerdrPaneSetChanged = onPaneSetChanged;
            _herdrChild = new HerdrPaneChild(
                client,
                _settings,
                _logger,
                liveAntiphonPanes: () => Array.Empty<HerdrPaneAllocator.LivePane>());
            // Re-bind the existing pane without re-launching: reconstruct the child's identity fields.
            await ((HerdrPaneChild)_herdrChild).AttachExistingAsync(sidecar, ct);
            _herdrChild.Exited += exit =>
            {
                lock (_gate)
                {
                    if (_status == "Exited") return;
                    _status = "Exited";
                    _exitCode = exit.ExitCode;
                    _exitReason = exit.Reason;
                }

                _clientReady.TrySetResult(false);
                _exited.TrySetResult();
                _events.Publish(
                    SessionRunnerEventNames.SessionExited,
                    new RunnerSessionExitedEvent(_sessionId, exit.ExitCode, exit.Reason, LastSequence: 0));
                _onHerdrPaneSetChanged?.Invoke();
            };
            lock (_gate)
                _status = "Running";
            _clientReady.TrySetResult(true);
            _events.Publish(
                SessionRunnerEventNames.SessionAdopted,
                new RunnerSessionAdoptedEvent(_sessionId, _childPid, _startedAt, LastSequence: 0));

            // Re-tail via existing TranscriptSidecar if present.
            var transcript = TranscriptSidecar.TryLoad(TranscriptSidecar.PathFor(_settings.SessionLogPath, _sessionId));
            if (transcript?.TranscriptPath is { } path)
            {
                _tailer = new TranscriptTailer(
                    _sessionId, transcript.Cwd ?? sidecar.Cwd ?? "", _events, _logger,
                    claims: _transcriptClaims,
                    inputLog: _inputLog,
                    childStartUtc: transcript.ChildStartUtc ?? sidecar.LaunchedAtUtc,
                    agentName: transcript.AgentName,
                    resumeLaunch: transcript.ResumeLaunch,
                    onBound: RecordTranscriptBinding);
                _tailer.Start();
            }
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
                    _adopted,
                    _herdrAgentStatus,
                    _herdrAgentStatusSinceUtc);
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
            // Herdr has no push stream in S2 — serve an on-demand pane.read when asked.
            if (_herdrChild is { } herdr)
            {
                try
                {
                    var screen = herdr.ReadScreenAsync(CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (screen is not null)
                    {
                        lock (_gate)
                        {
                            _lastSequence = Math.Max(_lastSequence, screen.Revision);
                            return new RunnerSnapshotDto(
                                _sessionId,
                                screen.Text,
                                screen.Text,
                                _lastSequence,
                                _startedAt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Herdr pane.read failed for snapshot of {SessionId}", _sessionId);
                }
            }

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
            // Recorded BEFORE the write: Claude cannot persist a prompt we have not sent yet, so
            // the input log is always ahead of the transcript record that rule C4 matches it to.
            _inputLog.Append(input);
            if (_herdrChild is { } herdr)
            {
                // Wait for LaunchAsync to finish (same _clientReady gate the pty path uses).
                if (!await _clientReady.Task.WaitAsync(ct))
                    throw new InvalidOperationException("Herdr session ended before it was ready for input.");
                await herdr.WriteAsync(input, ct);
                return;
            }

            var client = await AwaitClientAsync(ct);
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
            if (_herdrChild is { } herdr)
            {
                await herdr.ResizeAsync(cols, rows, ct);
                return;
            }

            var client = await AwaitClientAsync(ct);
            await client.ResizeAsync(cols, rows, ct);
        }

        public async Task KillAsync(TimeSpan timeout, CancellationToken ct, string? exitReasonOverride = null)
        {
            if (HasExited)
                return;

            if (exitReasonOverride is not null)
            {
                lock (_gate)
                    _exitReasonOverride = exitReasonOverride;
            }

            if (_herdrChild is { } herdr)
            {
                await herdr.KillAsync(ct);
                await Task.WhenAny(_exited.Task, Task.Delay(timeout + TimeSpan.FromSeconds(2), ct));
                return;
            }

            if (_client is not { } client)
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

        /// <summary>
        /// Relaunch prerequisite (CARD-0050): waits until this EXITED session's pty-host process is
        /// really gone, so a new host for the same session id cannot lose the pipe-name race to it.
        /// Ack-first (the normal path — the host deletes its manifest and exits), bounded wait,
        /// then a verified kill: with the child already exited the host protects nothing, and a
        /// lingering one only exists to reject the relaunch. Never throws.
        /// </summary>
        public async Task EnsureExitedHostGoneAsync(TimeSpan bound, CancellationToken ct)
        {
            if (!HasExited || _hostPid <= 0)
                return;

            await ShutdownHostAsync();

            var deadline = DateTime.UtcNow + bound;
            var killed = false;
            while (DateTime.UtcNow < deadline)
            {
                if (!HostProcessStillAlive())
                    return;
                if (!killed && DateTime.UtcNow + TimeSpan.FromSeconds(2) >= deadline)
                {
                    // The ack did not take (host wedged, or the ack raced its own pipe teardown) —
                    // escalate once, then keep waiting for the exit inside the same bound.
                    KillHostIfStillOurs();
                    killed = true;
                }

                try
                {
                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            _logger.LogWarning(
                "Exited session {SessionId}'s pty-host (pid {HostPid}) survived shutdown + kill within "
                + "{Bound}; the relaunch may race its pipe",
                _sessionId, _hostPid, bound);
        }

        private bool HostProcessStillAlive()
        {
            try
            {
                using var host = System.Diagnostics.Process.GetProcessById(_hostPid);
                // Pid reuse by an unrelated process counts as "gone" — never wait on a stranger.
                return !host.HasExited && host.ProcessName.Contains("PtyHost", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void KillHostIfStillOurs()
        {
            try
            {
                using var host = System.Diagnostics.Process.GetProcessById(_hostPid);
                if (!host.HasExited && host.ProcessName.Contains("PtyHost", StringComparison.OrdinalIgnoreCase))
                    host.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
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

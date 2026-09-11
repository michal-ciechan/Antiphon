using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.PtyHost;

/// <summary>
/// The host's single session: owns the ConPTY child via <see cref="PtyAgentRunner"/>, assigns
/// monotonic output sequence numbers, appends the ansi log, and keeps a bounded replay ring so a
/// restarted runner can re-attach without gaps. Deliberately interpretation-free: no screen
/// rendering, no transcript parsing - that logic lives in the (restartable) session-runner.
/// </summary>
public sealed class HostSession : IAsyncDisposable
{
    private readonly PtyHostOptions _options;
    private readonly HostLog _log;
    private readonly PtyAgentRunner _runner;
    private readonly object _gate = new();
    private readonly Queue<(long Seq, string Chunk)> _ring = new();
    private readonly TaskCompletionSource<string> _exitRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _ringChars;
    private long _lastSeq;
    private string _status = PtyHostStatus.WaitingForLaunch;
    private int? _exitCode;
    private string? _exitReason;
    private int _cols;
    private int _rows;
    private string? _ansiLogPath;
    private PtyHostManifest? _manifest;
    private ChannelWriter<PtyHostMessage>? _sink;
    private CancellationTokenSource? _launchTimeoutCts;
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly Guid _hostInstanceId = Guid.NewGuid();
    private readonly CancellationTokenSource _custodyLifetime = new();
    private HostCustodyJournal? _custody;
    private Task? _custodyObserver;
    private Task? _exitObserver;
    private bool _hostOutputFailed;
    private readonly IVerificationCustodyFiles? _custodyFiles;

    public HostSession(PtyHostOptions options, HostLog log, IVerificationCustodyFiles? custodyFiles = null)
    {
        _custodyFiles = custodyFiles;
        _options = options;
        _log = log;
        // CARD-0045: the backend comes from --pty-backend when the launcher stated one, and falls
        // back to ANTIPHON_PTY_BACKEND (null override) otherwise — so production, where the daemon
        // exports the variable, resolves exactly as it did before, while a caller that owns a
        // runtime (a test) can now say which pseudoconsole its sessions get without reaching for
        // the process environment it shares with everything else.
        _runner = new PtyAgentRunner(options.PtyBackend);
    }

    /// <summary>Completes when the host should exit; the result is the reason (for the log).</summary>
    public Task<string> ExitRequested => _exitRequested.Task;

    public string Status
    {
        get { lock (_gate) return _status; }
    }

    public void StartLaunchTimeout()
    {
        _launchTimeoutCts = new CancellationTokenSource();
        var token = _launchTimeoutCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.LaunchTimeout, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (_status != PtyHostStatus.WaitingForLaunch)
                    return;
            }

            // Runner died between spawning us and sending Launch - nothing to preserve.
            TryDeleteManifest();
            RequestExit("launch timeout - no Launch received");
        });
    }

    public async Task<PtyHostMessage> LaunchAsync(LaunchMessage launch, CancellationToken ct)
    {
        await _admission.WaitAsync(ct);
        try { return await LaunchCoreAsync(launch, ct); }
        finally { _admission.Release(); }
    }

    private async Task<PtyHostMessage> LaunchCoreAsync(LaunchMessage launch, CancellationToken ct)
    {
        if (_options.CustodyStoreRoot is not null && launch.VerificationBinding is null)
            return new ErrorMessage("verification_custody_binding_required", "Tracked hosts require their reserved execution binding.");
        lock (_gate)
        {
            if (_status != PtyHostStatus.WaitingForLaunch || _custody is not null)
                return new ErrorMessage("alreadyLaunched", $"Session is {_status}.");
            _cols = launch.Cols;
            _rows = launch.Rows;
            _ansiLogPath = launch.AnsiLogPath;
        }

        _launchTimeoutCts?.Cancel();
        _runner.OnData += OnData;

        try
        {
            if (launch.VerificationBinding is { } binding)
            {
                if (_options.CustodyStoreRoot is null || launch.RunnerStoreId is not { } storeId)
                    throw new VerificationCustodyException("verification_custody_missing_store");
                var store = new VerificationCustodyStore(_options.CustodyStoreRoot, storeId, _custodyFiles);
                if (binding.Generation.SessionId != _options.SessionId || binding.Creation.WorktreePath != launch.Cwd)
                    throw new VerificationCustodyException("verification_custody_identity_mismatch");
                foreach (var path in new[] { Environment.CurrentDirectory, _options.ManifestDir,
                             _options.LogFile ?? _options.ManifestDir, launch.AnsiLogPath })
                    store.RequireOutsideSnapshot(path, binding);
                _custody = new(store, binding, _hostInstanceId);
            }
            if (launch.GrokRulesReceipt is not null || _custody is not null)
            {
                _manifest = new PtyHostManifest
                {
                    SessionId = _options.SessionId, PipeName = _options.PipeName,
                    HostPid = Environment.ProcessId,
                    HostStartTimeUtc = TryGetProcessStartUtc(Environment.ProcessId) ?? DateTime.UtcNow,
                    LaunchPending = true, GrokRulesReceipt = launch.GrokRulesReceipt,
                    CreatedAtUtc = DateTime.UtcNow,
                    VerificationBinding = _custody?.Binding, VerificationHost = _custody?.Identity,
                    Cwd = launch.Cwd, Cols = launch.Cols, Rows = launch.Rows,
                    AnsiLogPath = launch.AnsiLogPath, TranscriptEnabled = launch.TranscriptEnabled,
                };
                _custody?.RecordManifest(_manifest, launched: false);
                _manifest.SaveAtomic(_options.ManifestPath);
            }
            if (_custody is not null)
                await _runner.StartTrackedAsync(launch.Exe, launch.Args.ToArray(), launch.Cwd,
                    launch.Env.ToDictionary(kv => kv.Key, kv => kv.Value), launch.Cols, launch.Rows,
                    launch.MemoryLimitMb, _custody, ct);
            else
                await _runner.StartAsync(
                launch.Exe,
                launch.Args.ToArray(),
                launch.Cwd,
                launch.Env.ToDictionary(kv => kv.Key, kv => kv.Value),
                launch.Cols,
                launch.Rows,
                launch.MemoryLimitMb,
                ct);
        }
        catch (Exception ex)
        {
            _log.Error("Launch failed", ex);
            if (_custody is not null)
            {
                try { _custody.RecordLaunchFailure(ex is PlatformNotSupportedException); }
                catch (Exception journalError) { _log.Error("Custody failure persistence failed", journalError); }
                StartLingerExpiry();
                // Keep the original observer/ledger available even after an uncertain native start.
                return new ErrorMessage(ex is PlatformNotSupportedException
                    ? "verification_custody_unsupported_backend" : "verification_custody_start_failed",
                    "Tracked launch failed; retained custody evidence must be reconciled.");
            }
            TryDeleteManifest();
            RequestExit("launch failed");
            return new ErrorMessage("launchFailed", ex.Message);
        }

        var childPid = _runner.Pid ?? 0;
        var childStart = TryGetProcessStartUtc(childPid) ?? _runner.StartedAt;

        lock (_gate)
        {
            _status = PtyHostStatus.Running;
        }

        var hostProcess = Environment.ProcessId;
        _manifest = new PtyHostManifest
        {
            SessionId = _options.SessionId,
            PipeName = _options.PipeName,
            HostPid = hostProcess,
            HostStartTimeUtc = TryGetProcessStartUtc(hostProcess) ?? DateTime.UtcNow,
            ChildPid = childPid,
            ChildStartTimeUtc = childStart,
            Exe = launch.Exe,
            Cwd = launch.Cwd,
            Cols = launch.Cols,
            Rows = launch.Rows,
            TranscriptEnabled = launch.TranscriptEnabled,
            GrokRulesReceipt = launch.GrokRulesReceipt,
            AnsiLogPath = launch.AnsiLogPath,
            CreatedAtUtc = DateTime.UtcNow,
            VerificationBinding = _custody?.Binding,
            VerificationHost = _custody?.Identity,
        };
        _custody?.RecordManifest(_manifest, launched: true);
        _manifest.SaveAtomic(_options.ManifestPath);
        // Which pseudoconsole this session got is the difference between a 43 KB body arriving whole
        // and arriving clipped at 1 KB, and it is invisible everywhere else — record it per host.
        _log.Info($"Launched {launch.Exe} (child pid {childPid}); pty backend: {_runner.Backend}");

        _exitObserver = ObserveExitAsync();
        return new LaunchedMessage(childPid, childStart);
    }

    /// <summary>
    /// Atomically replays ring chunks after <paramref name="lastSeq"/> into <paramref name="sink"/>
    /// and installs it as the live output sink - no gap, no duplicate. Returns a
    /// <see cref="ResyncMessage"/> instead if the requested point has fallen out of the ring.
    /// </summary>
    public PtyHostMessage? Attach(long lastSeq, ChannelWriter<PtyHostMessage> sink)
    {
        lock (_gate)
        {
            var firstAvailable = _ring.Count > 0 ? _ring.Peek().Seq : _lastSeq + 1;
            if (lastSeq + 1 < firstAvailable)
                return new ResyncMessage(firstAvailable, _lastSeq);

            sink.TryWrite(new AttachedMessage(lastSeq + 1, _lastSeq));
            foreach (var (seq, chunk) in _ring)
            {
                if (seq > lastSeq)
                    sink.TryWrite(new OutputMessage(seq, chunk));
            }

            _sink = sink;

            if (_status == PtyHostStatus.Exited)
                sink.TryWrite(new ExitedMessage(_exitCode, _exitReason ?? "Unknown", _lastSeq));
        }

        return null;
    }

    public void Detach(ChannelWriter<PtyHostMessage> sink)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_sink, sink))
                _sink = null;
        }
    }

    public Task WriteInputAsync(string data, CancellationToken ct) => _runner.WriteAsync(data, ct);

    public Task SendLineAsync(string line, CancellationToken ct) => _runner.SendLineAsync(line, ct);

    public void Resize(int cols, int rows)
    {
        _runner.Resize(cols, rows);
        lock (_gate)
        {
            _cols = cols;
            _rows = rows;
        }

        if (_manifest is not null)
        {
            _manifest = _manifest with { Cols = cols, Rows = rows };
            _manifest.SaveAtomic(_options.ManifestPath);
        }
    }

    public Task<bool> KillAsync(TimeSpan timeout) => _runner.KillAsync(timeout);

    public void ClearLiveBuffer() => _runner.ClearLiveBuffer();

    public StatusReplyMessage GetStatus()
    {
        lock (_gate)
        {
            return new StatusReplyMessage(
                _status,
                _runner.Pid,
                _manifest?.ChildStartTimeUtc,
                _cols,
                _rows,
                _lastSeq,
                _exitCode,
                _exitReason);
        }
    }

    public HelloAckMessage GetHelloAck(string hostVersion) =>
        new(PtyHostProtocol.Version, hostVersion, _options.SessionId, Status,
            _options.CustodyStoreRoot is not null
                && PtyBackendPolicy.Resolve(_options.PtyBackend).Backend == PtyBackend.ModernConPty
                ? ["verificationCustodyV1"] : [], _hostInstanceId);

    public Task<VerificationCustodyStatus> GetCustodyAsync(VerificationExecutionBinding binding,
        CancellationToken ct) => GetCustodyAsync(binding, false, ct);

    public async Task<VerificationCustodyStatus> GetCustodyAsync(VerificationExecutionBinding binding,
        bool seal, CancellationToken ct)
    {
        await _admission.WaitAsync(ct);
        try
        {
            if (_custody is null || _custody.Binding != binding)
                throw new VerificationCustodyException("verification_custody_identity_mismatch");
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            observation.CancelAfter(TimeSpan.FromSeconds(5));
            return await _custody.ObserveAsync(_runner, () => Volatile.Read(ref _hostOutputFailed),
                seal || Status == PtyHostStatus.Exited, observation.Token);
        }
        finally { _admission.Release(); }
    }

    /// <summary>Runner ack: fate recorded server-side; remove the manifest and exit.</summary>
    public void Shutdown()
    {
        _custody?.RequireAcceptedReceipt();
        TryDeleteManifest();
        RequestExit("shutdown ack from runner");
    }

    private void OnData(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_gate)
        {
            var seq = ++_lastSeq;
            _ring.Enqueue((seq, text));
            _ringChars += text.Length;
            while (_ringChars > _options.RingCapChars && _ring.Count > 1)
                _ringChars -= _ring.Dequeue().Chunk.Length;

            if (_ansiLogPath is not null)
            {
                try
                {
                    File.AppendAllText(_ansiLogPath, text);
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _hostOutputFailed, true);
                    _log.Error("ansi log append failed", ex);
                }
            }

            _sink?.TryWrite(new OutputMessage(seq, text));
        }
    }

    private async Task ObserveExitAsync()
    {
        int exitCode;
        string exitReason;
        try
        {
            exitCode = await _runner.Exited;
            exitReason = _runner.ExitReason.ToString();
        }
        catch (Exception ex)
        {
            _log.Error("exit observer failed", ex);
            exitCode = -1;
            exitReason = "ObserverFailed";
        }

        long lastSeq;
        lock (_gate)
        {
            _status = PtyHostStatus.Exited;
            _exitCode = exitCode;
            _exitReason = exitReason;
            lastSeq = _lastSeq;
        }

        // Arm this before touching the manifest: a fixture or external cleanup can remove its
        // directory between child exit and SaveAtomic, but that must never make this host immortal.
        StartLingerExpiry();

        if (_custody is not null)
            _custodyObserver = ObserveCustodyUntilFinalAsync(_custodyLifetime.Token);

        if (_manifest is not null)
        {
            _manifest = _manifest with
            {
                ExitCode = exitCode,
                ExitReason = exitReason,
                ExitedAtUtc = DateTime.UtcNow,
            };
            try { _manifest.SaveAtomic(_options.ManifestPath); }
            catch (Exception ex) { _log.Error("exit manifest save failed; lingering anyway", ex); }
        }

        _log.Info($"Child exited (code {exitCode}, reason {exitReason}); lingering for runner ack");
        lock (_gate)
        {
            _sink?.TryWrite(new ExitedMessage(exitCode, exitReason, lastSeq));
        }

    }

    private async Task ObserveCustodyUntilFinalAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var status = await GetCustodyAsync(_custody!.Binding, ct);
                if (status.Receipt is not null) return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.Error("Custody observation failed; evidence retained", ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    private void RequestExit(string reason) => _exitRequested.TrySetResult(reason);

    private void StartLingerExpiry() => _ = Task.Run(async () =>
    {
        await Task.Delay(_options.LingerTtl);
        RequestExit("linger TTL expired without runner ack");
    });

    private void TryDeleteManifest()
    {
        try
        {
            File.Delete(_options.ManifestPath);
        }
        catch
        {
            // Best-effort; a stale manifest is handled by the runner's sweep (dead host pid).
        }
    }

    private static DateTime? TryGetProcessStartUtc(int pid)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(pid).StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _custodyLifetime.Cancel();
        if (_custodyObserver is not null)
            await _custodyObserver;
        _launchTimeoutCts?.Cancel();
        _launchTimeoutCts?.Dispose();
        _runner.OnData -= OnData;
        await _runner.DisposeAsync();
        if (_exitObserver is not null)
            await _exitObserver;
        if (_custodyObserver is not null)
            await _custodyObserver;
        _custodyLifetime.Dispose();
        _admission.Dispose();
    }
}

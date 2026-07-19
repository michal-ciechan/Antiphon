using System.Threading.Channels;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;

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
    private readonly PtyAgentRunner _runner = new();
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

    public HostSession(PtyHostOptions options, HostLog log)
    {
        _options = options;
        _log = log;
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
        lock (_gate)
        {
            if (_status != PtyHostStatus.WaitingForLaunch)
                return new ErrorMessage("alreadyLaunched", $"Session is {_status}.");
            _cols = launch.Cols;
            _rows = launch.Rows;
            _ansiLogPath = launch.AnsiLogPath;
        }

        _launchTimeoutCts?.Cancel();
        _runner.OnData += OnData;

        try
        {
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
            AnsiLogPath = launch.AnsiLogPath,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _manifest.SaveAtomic(_options.ManifestPath);
        _log.Info($"Launched {launch.Exe} (child pid {childPid})");

        _ = ObserveExitAsync();
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
        new(PtyHostProtocol.Version, hostVersion, _options.SessionId, Status);

    /// <summary>Runner ack: fate recorded server-side; remove the manifest and exit.</summary>
    public void Shutdown()
    {
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

        if (_manifest is not null)
        {
            _manifest = _manifest with
            {
                ExitCode = exitCode,
                ExitReason = exitReason,
                ExitedAtUtc = DateTime.UtcNow,
            };
            _manifest.SaveAtomic(_options.ManifestPath);
        }

        _log.Info($"Child exited (code {exitCode}, reason {exitReason}); lingering for runner ack");
        lock (_gate)
        {
            _sink?.TryWrite(new ExitedMessage(exitCode, exitReason, lastSeq));
        }

        // Linger so a restarted runner can collect the exit; TTL bounds orphan lifetime.
        _ = Task.Run(async () =>
        {
            await Task.Delay(_options.LingerTtl);
            RequestExit("linger TTL expired without runner ack");
        });
    }

    private void RequestExit(string reason) => _exitRequested.TrySetResult(reason);

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
        _launchTimeoutCts?.Cancel();
        _launchTimeoutCts?.Dispose();
        _runner.OnData -= OnData;
        await _runner.DisposeAsync();
    }
}

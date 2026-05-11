using System.Text;
using Porta.Pty;

namespace Antiphon.Agents.Pty;

public sealed class PtyAgentRunner : IAsyncDisposable
{
    private IPtyConnection? _conn;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly StringBuilder _liveBuffer = new();
    private readonly object _bufferLock = new();
    private readonly TaskCompletionSource<int> _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PtySessionAudit? _audit;
    private DateTime _startedAt;
    private TerminalScreen? _screen;

    public RingBuffer<string> Output { get; } = new(4096);

    public event Action<string>? OnData;

    public Task<int> Exited => _exitTcs.Task;

    public int? Pid => _conn?.Pid;

    public DateTime StartedAt => _startedAt;

    /// <summary>Path to the audit directory written for this session.</summary>
    public string? AuditDirectory => _audit?.Directory;

    public async Task StartAsync(
        string app,
        string[] commandLine,
        string? cwd = null,
        IDictionary<string, string>? env = null,
        int cols = 120,
        int rows = 30,
        CancellationToken ct = default)
    {
        if (_conn is not null) throw new InvalidOperationException("Already started");
        _startedAt = DateTime.UtcNow;

        var options = new PtyOptions
        {
            Name = "antiphon-pty",
            Cols = cols,
            Rows = rows,
            Cwd = cwd ?? Environment.CurrentDirectory,
            App = app,
            CommandLine = commandLine,
            Environment = env ?? new Dictionary<string, string>(),
        };

        _screen = new TerminalScreen(cols, rows);

        _conn = await PtyProvider.SpawnAsync(options, ct);
        _conn.ProcessExited += (_, e) =>
        {
            _exitTcs.TrySetResult(e.ExitCode);
        };

        _audit = PtySessionAudit.Create(app, commandLine, cwd, env, SnapshotText);

        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_conn is null) return;
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _conn.ReaderStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (n <= 0) break;
                var chunk = Encoding.UTF8.GetString(buffer, 0, n);
                Output.Add(chunk);
                lock (_bufferLock)
                {
                    _liveBuffer.Append(chunk);
                    _screen?.Feed(chunk);
                }
                _audit?.RecordChunk(chunk);
                OnData?.Invoke(chunk);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    public async Task WriteAsync(string data, CancellationToken ct = default)
    {
        if (_conn is null) throw new InvalidOperationException("Not started");
        var bytes = Encoding.UTF8.GetBytes(data);
        await _conn.WriterStream.WriteAsync(bytes.AsMemory(), ct);
        await _conn.WriterStream.FlushAsync(ct);
    }

    public async Task SendLineAsync(string line, CancellationToken ct = default)
    {
        await WriteAsync(line, ct);
        // Flush the message text first, then send Enter in a separate write.
        // ConPTY may drop the trailing \r if the entire line + Enter is sent as one
        // large chunk that exceeds its internal input-record queue capacity.
        await Task.Delay(20, ct);
        await WriteAsync("\r", ct);
    }

    public string SnapshotText()
    {
        lock (_bufferLock) return _liveBuffer.ToString();
    }

    /// <summary>
    /// Returns the current rendered screen state as a multi-line string.
    /// Each row is newline-separated with trailing spaces trimmed.
    /// Unlike <see cref="SnapshotText"/>, this correctly reflects cursor-forward
    /// optimisations and erase sequences — the text matches what a human sees.
    /// </summary>
    public string SnapshotScreen()
    {
        lock (_bufferLock) return _screen?.GetScreenText() ?? "";
    }

    /// <summary>
    /// Returns the rendered text of a specific screen row (0-based), trailing spaces trimmed.
    /// Useful for checking individual lines of an interactive menu.
    /// </summary>
    public string SnapshotRow(int row)
    {
        lock (_bufferLock) return _screen?.GetRow(row) ?? "";
    }

    public void ClearLiveBuffer()
    {
        lock (_bufferLock) _liveBuffer.Clear();
    }

    /// <summary>
    /// Polls until <paramref name="predicate"/> returns true for the rendered screen text,
    /// or until <paramref name="timeout"/> elapses.
    /// </summary>
    public async Task<bool> WaitForScreenAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(SnapshotScreen())) return true;
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    public async Task<bool> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(SnapshotText())) return true;
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    public async Task<bool> WaitForQuietAsync(
        TimeSpan quietPeriod,
        TimeSpan maxWait,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + maxWait;
        int lastLen;
        DateTime lastChange = DateTime.UtcNow;
        lock (_bufferLock) lastLen = _liveBuffer.Length;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, ct);
            int curLen;
            lock (_bufferLock) curLen = _liveBuffer.Length;
            if (curLen != lastLen)
            {
                lastLen = curLen;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange >= quietPeriod)
            {
                return true;
            }
        }
        return false;
    }

    public void Resize(int cols, int rows)
    {
        if (_conn is null) throw new InvalidOperationException("Not started");
        if (cols <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        _conn.Resize(cols, rows);
    }

    public async Task<bool> KillAsync(TimeSpan timeout)
    {
        if (_conn is null) return true;
        _conn.Kill();
        var done = await Task.WhenAny(_exitTcs.Task, Task.Delay(timeout));
        return done == _exitTcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        try { _readCts?.Cancel(); } catch { }
        if (_readTask is not null)
        {
            try { await _readTask; } catch { }
        }
        _conn?.Dispose();
        _readCts?.Dispose();
        if (_audit is not null) await _audit.DisposeAsync();
    }

}

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

    public RingBuffer<string> Output { get; } = new(4096);

    public event Action<string>? OnData;

    public Task<int> Exited => _exitTcs.Task;

    public int? Pid => _conn?.Pid;

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

        _conn = await PtyProvider.SpawnAsync(options, ct);
        _conn.ProcessExited += (_, e) =>
        {
            _exitTcs.TrySetResult(e.ExitCode);
        };

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
                lock (_bufferLock) _liveBuffer.Append(chunk);
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

    public Task SendLineAsync(string line, CancellationToken ct = default)
        => WriteAsync(line + "\r", ct);

    public string SnapshotText()
    {
        lock (_bufferLock) return _liveBuffer.ToString();
    }

    public void ClearLiveBuffer()
    {
        lock (_bufferLock) _liveBuffer.Clear();
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
    }

}

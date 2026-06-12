using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Tails an agent's Claude Code JSONL session transcript (<c>~/.claude/projects/&lt;slug&gt;/&lt;sessionId&gt;.jsonl</c>,
/// located by the session id we pass via <c>--session-id</c>), normalizes each appended line into
/// structured <see cref="RunnerTranscriptEvent"/>s, and publishes them on the session-runner event hub.
/// Reading always starts at offset 0 of the append-only file, so per-session sequence numbers are stable
/// across re-tails and consumers can de-duplicate on (SessionId, Sequence).
/// </summary>
internal sealed class TranscriptTailer : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LocatePollInterval = TimeSpan.FromSeconds(1);
    private const int MaxReadChunkBytes = 1 << 20; // 1 MiB per poll

    private readonly Guid _sessionId;
    private readonly string _cwd;
    private readonly SessionRunnerEventHub _events;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<RunnerTranscriptEvent> _entries = new();
    private Task? _loop;
    private long _seq;

    public TranscriptTailer(Guid sessionId, string cwd, SessionRunnerEventHub events, ILogger logger)
    {
        _sessionId = sessionId;
        _cwd = cwd;
        _events = events;
        _logger = logger;
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Full ordered snapshot of everything parsed so far (for catch-up after a missed stream).</summary>
    public RunnerTranscriptDto Snapshot()
    {
        lock (_gate)
            return new RunnerTranscriptDto(_sessionId, _entries.ToArray(), _seq);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var path = await LocateAsync(ct);
            if (path is null)
                return; // cancelled before the transcript file appeared (session ended)

            _logger.LogInformation("Tailing transcript {Path} for session {SessionId}", path, _sessionId);

            long offset = 0;
            var pending = new List<byte>();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > offset)
                    {
                        byte[] buffer;
                        int read;
                        await using (var fs = new FileStream(
                            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            fs.Seek(offset, SeekOrigin.Begin);
                            var len = (int)Math.Min(info.Length - offset, MaxReadChunkBytes);
                            buffer = new byte[len];
                            read = await fs.ReadAsync(buffer.AsMemory(0, len), ct);
                        }

                        if (read > 0)
                        {
                            offset += read;
                            pending.AddRange(read == buffer.Length ? buffer : buffer[..read]);
                            ProcessPending(pending);
                        }
                    }
                }
                catch (IOException)
                {
                    // File is mid-write / transiently locked — retry on the next poll.
                }

                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transcript tailer failed for session {SessionId}", _sessionId);
        }
    }

    // Split the accumulated bytes on '\n' (never part of a UTF-8 multi-byte sequence), decode and
    // emit each complete line, and keep the trailing partial line for the next read.
    private void ProcessPending(List<byte> pending)
    {
        var start = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            if (pending[i] != (byte)'\n')
                continue;

            var count = i - start;
            if (count > 0)
            {
                var line = Encoding.UTF8.GetString(pending.GetRange(start, count).ToArray()).TrimEnd('\r');
                EmitLine(line);
            }
            start = i + 1;
        }

        if (start > 0)
            pending.RemoveRange(0, start);
    }

    private void EmitLine(string line)
    {
        IReadOnlyList<TranscriptPart> parts;
        try { parts = TranscriptNormalizer.Normalize(line); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to normalize transcript line for session {SessionId}", _sessionId);
            return;
        }

        foreach (var p in parts)
        {
            RunnerTranscriptEvent evt;
            lock (_gate)
            {
                evt = new RunnerTranscriptEvent(
                    _sessionId, ++_seq, p.Kind, p.Uuid, p.ParentUuid, p.Timestamp,
                    p.Role, p.Text, p.ToolName, p.ToolInput, p.ToolUseId, p.ToolIsError, p.StopReason);
                _entries.Add(evt);
            }
            _events.Publish(SessionRunnerEventNames.SessionTranscript, evt);
        }
    }

    // Poll for the session's JSONL until it appears or the session ends (cancellation). Claude creates
    // the file lazily — for an interactive session, not until the human sends the first prompt — so we
    // must wait for the whole session lifetime, never give up early.
    private async Task<string?> LocateAsync(CancellationToken ct)
    {
        var fileName = _sessionId.ToString("D") + ".jsonl";
        var projectsRoot = ResolveProjectsRoot();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(projectsRoot))
                {
                    foreach (var dir in Directory.EnumerateDirectories(projectsRoot))
                    {
                        var candidate = Path.Combine(dir, fileName);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            try { await Task.Delay(LocatePollInterval, ct); }
            catch (OperationCanceledException) { return null; }
        }

        return null;
    }

    // Mirror Claude Code's transcript root: CLAUDE_CONFIG_DIR if set, else ~/.claude — then /projects.
    private static string ResolveProjectsRoot()
    {
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var root = !string.IsNullOrWhiteSpace(configDir)
            ? configDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(root, "projects");
    }

    public async ValueTask DisposeAsync()
    {
        try { await _cts.CancelAsync(); } catch (ObjectDisposedException) { }
        if (_loop is not null)
        {
            try { await _loop; }
            catch { /* loop already logged */ }
        }
        _cts.Dispose();
    }
}

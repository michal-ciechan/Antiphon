using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Tails an agent's Claude Code JSONL session transcript, normalizes each appended line into
/// structured <see cref="RunnerTranscriptEvent"/>s, and publishes them on the session-runner event hub.
/// Reading always starts at offset 0 of the append-only file, so per-session sequence numbers are stable
/// across re-tails and consumers can de-duplicate on (SessionId, Sequence).
///
/// The transcript file <em>should</em> be <c>~/.claude/projects/&lt;enc-cwd&gt;/&lt;sessionId&gt;.jsonl</c>
/// (the id we pass via <c>--session-id</c>), but interactive Claude does not reliably honour
/// <c>--session-id</c> — it can write the conversation to a self-chosen <c>&lt;uuid&gt;.jsonl</c>
/// instead (observed 2026-07-22; not env/flag dependent). So after a grace period the tailer FALLS
/// BACK to discovering the real file by its <c>cwd</c> field: the newest transcript whose recorded
/// cwd matches this session's, preferring one that appeared after the tailer started. Without this,
/// turn-end detection and channel reply routing silently break whenever Claude forks the id.
/// </summary>
internal sealed class TranscriptTailer : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LocatePollInterval = TimeSpan.FromSeconds(1);
    // How long to wait for the exact <session-id>.jsonl before falling back to cwd-based discovery.
    private static readonly TimeSpan ExactIdGrace = TimeSpan.FromSeconds(10);
    private const int MaxReadChunkBytes = 1 << 20; // 1 MiB per poll
    private const int CwdProbeLines = 25; // how many leading lines to scan for the "cwd" field

    private readonly Guid _sessionId;
    private readonly string _cwd;
    private readonly SessionRunnerEventHub _events;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<RunnerTranscriptEvent> _entries = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
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
    // the file lazily — for an interactive session, not until the first prompt is submitted — so we
    // must wait for the whole session lifetime, never give up early. The exact <session-id>.jsonl is
    // preferred; if it never appears (Claude forked the id) we fall back to cwd-based discovery.
    private async Task<string?> LocateAsync(CancellationToken ct)
    {
        var fileName = _sessionId.ToString("D") + ".jsonl";
        var projectsRoot = ResolveProjectsRoot();

        // Files that already existed when we started — the forked file (if any) will NOT be among
        // them, so preferring a not-previously-seen file disambiguates a fresh fork from stale
        // transcripts of earlier sessions in the same cwd.
        var preexisting = SnapshotJsonlPaths(projectsRoot);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(projectsRoot))
                {
                    // Fast path: Claude honoured --session-id.
                    foreach (var dir in Directory.EnumerateDirectories(projectsRoot))
                    {
                        var candidate = Path.Combine(dir, fileName);
                        if (File.Exists(candidate))
                            return candidate;
                    }

                    // Fallback: Claude forked to a self-chosen id. Discover by cwd once the exact
                    // file has had a fair chance to appear.
                    if (DateTime.UtcNow - _startedAtUtc >= ExactIdGrace
                        && DiscoverByCwd(projectsRoot, preexisting) is { } discovered)
                    {
                        _logger.LogWarning(
                            "Session {SessionId}: <session-id>.jsonl never appeared (Claude forked the id); "
                            + "adopting discovered transcript {Path} by cwd match ({Cwd})",
                            _sessionId, discovered, _cwd);
                        return discovered;
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

    private static HashSet<string> SnapshotJsonlPaths(string projectsRoot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(projectsRoot))
                foreach (var f in Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories))
                    set.Add(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return set;
    }

    // The transcript whose recorded cwd matches this session's, newest first, preferring one that
    // appeared after the tailer started (a fresh fork). If none is new (re-adoption of a session
    // whose forked file already existed), the newest cwd match overall is used.
    private string? DiscoverByCwd(string projectsRoot, HashSet<string> preexisting)
    {
        var matches = new List<(string Path, DateTime Mtime, bool IsNew)>();
        foreach (var file in Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            if (Path.GetFileNameWithoutExtension(file).Equals(_sessionId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                continue; // the exact file is handled by the fast path
            if (!TranscriptCwdMatches(file))
                continue;
            DateTime mtime;
            try { mtime = File.GetLastWriteTimeUtc(file); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            matches.Add((file, mtime, !preexisting.Contains(file)));
        }

        if (matches.Count == 0)
            return null;

        // Prefer files that appeared since we started (fresh fork); within each group, newest wins.
        return matches
            .OrderByDescending(m => m.IsNew)
            .ThenByDescending(m => m.Mtime)
            .First().Path;
    }

    private bool TranscriptCwdMatches(string file)
    {
        try
        {
            var canonicalCwd = Path.GetFullPath(_cwd);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            using var reader = new StreamReader(new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (var i = 0; i < CwdProbeLines; i++)
            {
                var line = reader.ReadLine();
                if (line is null) break;
                if (line.Length == 0 || line.IndexOf("\"cwd\"", StringComparison.Ordinal) < 0)
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("cwd", out var cwdEl)
                        && cwdEl.ValueKind == JsonValueKind.String
                        && cwdEl.GetString() is { } fileCwd)
                    {
                        return string.Equals(Path.GetFullPath(fileCwd), canonicalCwd, comparison);
                    }
                }
                catch (JsonException) { /* partial line mid-write — try the next */ }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { /* bad path in cwd field */ }
        return false;
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

using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Tails a Grok Build session's ACP update stream
/// (<c>GROK_HOME/sessions/&lt;url-enc-cwd&gt;/&lt;session-id&gt;/updates.jsonl</c>), normalizes each
/// appended row via <see cref="GrokTranscriptNormalizer"/>, and publishes the parts on the
/// session-runner event hub — the Grok counterpart of <see cref="TranscriptTailer"/> (CARD-0080 S2).
///
/// Deliberately WITHOUT the Claude tailer's discovery, claim-registry, fork-follow and adoption
/// machinery: grok honours <c>--session-id</c> (measured 1.0.5), so the path is known before launch
/// and the CARD-0006 hazard class — heuristically binding a stranger's conversation — cannot arise.
/// The only file this tailer will ever read is the one whose directory name IS this session's id.
///
/// Measured facts the read loop leans on (CARD-0080 S1):
/// <list type="bullet">
/// <item>The file is created LAZILY at the first submit (~1.1s after Enter), so "missing" is the
/// normal state for as long as nobody types; only a child that exits after input was delivered
/// without the file ever appearing is a fault.</item>
/// <item>Grok holds <c>updates.jsonl</c> (plus a <c>.lock</c>) open for the whole session — reads
/// must share write/delete access.</item>
/// <item>Rows are line-buffered and flushed per update (turn_completed ~90ms after the screen's
/// done line) — no Claude-style 45s flush stall to design around.</item>
/// </list>
///
/// Reading always restarts at offset 0 on a re-tail, exactly like the Claude tailer: sequences are
/// deterministic per file content, so consumers de-duplicate on (SessionId, Uuid/Sequence).
/// </summary>
internal sealed class GrokTranscriptTailer : ITranscriptTailer
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(300);
    // A dead child appends no more rows; give the last line-buffered flush a moment, then stop.
    private static readonly TimeSpan ChildExitSettle = TimeSpan.FromSeconds(3);
    private const int MaxReadChunkBytes = 1 << 20; // 1 MiB per poll

    private readonly Guid _sessionId;
    private readonly string _updatesPath;
    private readonly SessionRunnerEventHub _events;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly SessionInputLog? _inputLog;
    private readonly GrokTranscriptNormalizer _normalizer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<RunnerTranscriptEvent> _entries = new();
    private Task? _loop;
    private long _seq;
    private DateTime? _childExitedAtUtc;

    public GrokTranscriptTailer(
        Guid sessionId,
        string updatesPath,
        SessionRunnerEventHub events,
        ILogger logger,
        TimeSpan? pollInterval = null,
        SessionInputLog? inputLog = null)
    {
        _sessionId = sessionId;
        _updatesPath = updatesPath;
        _events = events;
        _logger = logger;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _inputLog = inputLog;
    }

    /// <summary>The (deterministic) file this tailer reads — for the sidecar and diagnostics.</summary>
    public string UpdatesPath => _updatesPath;

    /// <summary>
    /// Where grok will write this session's update stream:
    /// <c>{GROK_HOME}/sessions/{Uri.EscapeDataString(full-cwd)}/{sessionId:D}/updates.jsonl</c>
    /// (verified layout, grok 1.0.5). GROK_HOME resolves from the launch env first — that is the
    /// environment the CHILD actually sees — then this process's own, then <c>~/.grok</c>.
    /// </summary>
    public static string ResolveUpdatesPath(
        IReadOnlyDictionary<string, string>? launchEnv, string cwd, Guid sessionId)
    {
        string? grokHome = null;
        launchEnv?.TryGetValue("GROK_HOME", out grokHome);
        if (string.IsNullOrWhiteSpace(grokHome))
            grokHome = Environment.GetEnvironmentVariable("GROK_HOME");
        if (string.IsNullOrWhiteSpace(grokHome))
            grokHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok");

        return Path.Combine(
            grokHome,
            "sessions",
            Uri.EscapeDataString(Path.GetFullPath(cwd)),
            sessionId.ToString("D"),
            "updates.jsonl");
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void NotifyChildExited() => _childExitedAtUtc ??= DateTime.UtcNow;

    public RunnerTranscriptDto Snapshot()
    {
        lock (_gate)
            return new RunnerTranscriptDto(_sessionId, _entries.ToArray(), _seq);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "Tailing Grok updates {Path} for session {SessionId} (deterministic path; created lazily at first submit)",
                _updatesPath, _sessionId);

            long offset = 0;
            var pending = new List<byte>();
            var everExisted = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var info = new FileInfo(_updatesPath);
                    if (info.Exists)
                    {
                        everExisted = true;
                        if (info.Length > offset)
                        {
                            byte[] buffer;
                            int read;
                            // Grok keeps the file open for the session's lifetime; share everything.
                            await using (var fs = new FileStream(
                                _updatesPath, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete))
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
                }
                catch (IOException)
                {
                    // Mid-write / transiently locked — retry on the next poll.
                }

                if (_childExitedAtUtc is { } exitedAt && DateTime.UtcNow - exitedAt >= ChildExitSettle)
                {
                    // Chunks whose turn_completed never arrived (child died mid-turn) are emitted
                    // rather than lost; no TurnEnd is synthesized — the relaunch path's
                    // SessionRestartBoundary is what ends a turn the process abandoned.
                    Publish(_normalizer.FlushPending());
                    if (!everExisted)
                        ReportMissingAfterChildExit();
                    return;
                }

                await Task.Delay(_pollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Grok transcript tailer failed for session {SessionId}", _sessionId);
        }
    }

    // Split the accumulated bytes on '\n' (never part of a UTF-8 multi-byte sequence), normalize
    // each complete line, and keep the trailing partial line for the next read — a half-written
    // row while grok appends is normal, not an error.
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
        try { parts = _normalizer.Normalize(line); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to normalize Grok update row for session {SessionId}", _sessionId);
            return;
        }

        Publish(parts);
    }

    private void Publish(IReadOnlyList<TranscriptPart> parts)
    {
        foreach (var p in parts)
        {
            RunnerTranscriptEvent evt;
            lock (_gate)
            {
                evt = new RunnerTranscriptEvent(
                    _sessionId, ++_seq, p.Kind, p.Uuid, p.ParentUuid, p.Timestamp,
                    p.Role, p.Text, p.ToolName, p.ToolInput, p.ToolUseId, p.ToolIsError, p.StopReason,
                    p.ApiCallId, p.InputTokens, p.OutputTokens, p.CacheReadTokens, p.CacheCreationTokens,
                    p.IsApiError, p.ApiErrorClass, p.ApiErrorStatus, p.Model, p.ModelCalls);
                _entries.Add(evt);
            }
            _events.Publish(SessionRunnerEventNames.SessionTranscript, evt);
        }
    }

    // Same rule as the Claude tailer: only a fault if input was actually delivered — a session
    // nobody typed at legitimately never creates the file (lazy creation at first submit).
    private void ReportMissingAfterChildExit()
    {
        if (_inputLog is null || _inputLog.IsEmpty)
            return;

        _logger.LogWarning(
            "Session {SessionId}: the child exited without ever creating {Path}, although input was "
            + "delivered to it. Nothing was ingested for this session.",
            _sessionId, _updatesPath);

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.TranscriptMissing,
                $"The session's child process exited without creating its Grok update stream at {_updatesPath}, "
                + "although input had been delivered to it.",
                _updatesPath));
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

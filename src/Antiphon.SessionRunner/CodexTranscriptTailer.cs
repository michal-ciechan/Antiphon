using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Tails a Codex session's rollout JSONL
/// (<c>CODEX_HOME/sessions/YYYY/MM/DD/rollout-&lt;local-ts&gt;-&lt;session-uuid&gt;.jsonl</c>),
/// normalizes each appended row via <see cref="CodexTranscriptNormalizer"/>, and publishes the
/// parts on the session-runner event hub (CARD-0099 S1).
///
/// <para><b>Discovery is unavoidable and therefore obeys CARD-0006 in full.</b> Grok's tailer can
/// skip all of this because grok honours <c>--session-id</c>; Codex has no such flag (checked
/// against <c>codex --help</c>, 0.147.0 — <c>resume</c>/<c>fork</c> take an id, a fresh launch
/// cannot be given one) and the interactive TUI never prints its session id on screen either
/// (measured: <c>codex exec</c> prints <c>session id: &lt;uuid&gt;</c> at startup, the TUI prints
/// nothing of the sort — its banner carries version, model, directory and permissions only). So
/// the rollout has to be found, and finding it by "newest file in this cwd" is precisely the move
/// that bound an agent to the operator's own conversation on 2026-08-09. Every bind here carries
/// positive evidence:</para>
/// <list type="bullet">
/// <item><b>C1</b> — no other live session has claimed the file
/// (<see cref="TranscriptClaimRegistry"/>, shared with the Claude tailer, so the two can never
/// fight over one path).</item>
/// <item><b>C2</b> — <c>session_meta.cwd</c> is this session's cwd. EXACT for Codex: the cwd is a
/// recorded field on line 0, not an encoded directory name.</item>
/// <item><b>C3</b> — the first timestamped record is not older than the child process, waived on a
/// resume launch whose copied history legitimately predates the relaunch. Also exact for Codex:
/// <c>session_meta</c> carries the session's own start time.</item>
/// <item><b>C4</b> — some user prompt in it is text this session actually received
/// (<see cref="SessionInputLog"/>). Mandatory for every bind, exactly as it is for Claude, and
/// still the only positive identification available: no rollout record carries a pid.</item>
/// </list>
/// There is no C2b analogue — Codex has no <c>--name</c> and writes no agent-name record — and
/// nothing here substitutes a weaker rule for it. <c>session_meta.originator</c> looks like a
/// discriminator but is not one: a human running <c>codex</c> in the same checkout produces the
/// same <c>codex-tui</c> value we do, so it is reported in refusals and never gated on.
///
/// <para>Nothing qualifying means the session runs WITHOUT a transcript and raises a visible
/// fault — never a bind on cwd and recency alone.</para>
///
/// <para>Measured facts the read loop leans on (all 2026-08-20, codex-cli 0.147.0, real TUI
/// sessions through a modern ConPTY):</para>
/// <list type="bullet">
/// <item>The rollout is created <b>LAZILY, at the first submit</b> — a session left up for 30 s
/// with a rendered idle composer and zero bytes written produced no file at all. "Missing" is the
/// normal state for as long as nobody types, exactly like Claude's; only a child that exits after
/// input was delivered without any file appearing is a fault.</item>
/// <item>Codex holds the rollout open for the session's lifetime: a plain read throws
/// <c>IOException: being used by another process</c>. Reads must share write and delete.</item>
/// <item>Rows are flushed per event (the <c>task_complete</c> lands within ~300 ms of the screen
/// showing the answer) — no Claude-style multi-second flush stall to design around.</item>
/// <item>A child killed mid-turn leaves the turn with NO <c>task_complete</c>. Nothing synthesizes
/// one here: the relaunch path's <c>SessionRestartBoundary</c> is what ends a turn the process
/// abandoned, same as for Grok.</item>
/// </list>
///
/// Reading always restarts at offset 0 on a re-tail, exactly like the other two tailers: uuids are
/// deterministic per file content, so consumers de-duplicate on (SessionId, Uuid/Sequence).
/// </summary>
internal sealed class CodexTranscriptTailer : ITranscriptTailer
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DefaultLocatePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultRefusalFaultDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultRefusalFaultRepeat = TimeSpan.FromMinutes(5);
    // A dead child appends no more rows; give the last flush a moment, then stop.
    private static readonly TimeSpan ChildExitSettle = TimeSpan.FromSeconds(3);
    // Slack on C3 for clock skew between the process-start stamp and Codex's own timestamps.
    private static readonly TimeSpan EpochSkewSlack = TimeSpan.FromSeconds(2);
    private const int MaxReadChunkBytes = 1 << 20; // 1 MiB per poll

    private readonly Guid _sessionId;
    private readonly string _cwd;
    private readonly SessionRunnerEventHub _events;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _locatePollInterval;
    private readonly TimeSpan _refusalFaultDelay;
    private readonly TimeSpan _refusalFaultRepeat;
    private readonly TranscriptClaimRegistry? _claims;
    private readonly SessionInputLog? _inputLog;
    private readonly DateTime? _firstInputUtc;
    private readonly DateTime? _childStartUtc;
    private readonly bool _resumeLaunch;
    private string? _knownTranscriptPath;
    private readonly string _sessionsRoot;
    private readonly Action<string, string>? _onBound;
    private readonly Action? _onUnbound;
    private volatile bool _claimRevoked;
    private Guid _claimRevokedBy;
    private readonly CodexTranscriptNormalizer _normalizer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<RunnerTranscriptEvent> _entries = new();
    private readonly Dictionary<string, CodexRolloutProbe> _probes = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;
    private long _seq;
    private DateTime? _childExitedAtUtc;

    /// <param name="claims">Process-wide "who is tailing what" registry (rule C1). Null disables the check.</param>
    /// <param name="inputLog">What this session was sent — the evidence for rule C4. Null means no
    /// bind can ever be justified, which is the safe default for a session with no input record.</param>
    /// <param name="firstInputUtc">Persisted first delivered-input time from the sidecar after runner adoption.</param>
    /// <param name="childStartUtc">Child process start, the epoch for rule C3.</param>
    /// <param name="resumeLaunch">True for <c>codex resume</c>/<c>fork</c>: waives C3.</param>
    /// <param name="knownTranscriptPath">Sidecar-recorded path (restart re-adopt): re-tailed directly, no discovery.</param>
    /// <param name="sessionsRoot">Override for <c>CODEX_HOME/sessions</c> (tests).</param>
    /// <param name="onBound">Called with (path, how) whenever a rollout is bound, so the sidecar can record it.</param>
    public CodexTranscriptTailer(
        Guid sessionId,
        string cwd,
        SessionRunnerEventHub events,
        ILogger logger,
        TimeSpan? pollInterval = null,
        TimeSpan? locatePollInterval = null,
        TranscriptClaimRegistry? claims = null,
        SessionInputLog? inputLog = null,
        DateTime? firstInputUtc = null,
        DateTime? childStartUtc = null,
        bool resumeLaunch = false,
        string? knownTranscriptPath = null,
        string? sessionsRoot = null,
        Action<string, string>? onBound = null,
        Action? onUnbound = null,
        TimeSpan? refusalFaultDelay = null,
        TimeSpan? refusalFaultRepeat = null)
    {
        _sessionId = sessionId;
        _cwd = cwd;
        _events = events;
        _logger = logger;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _locatePollInterval = locatePollInterval ?? DefaultLocatePollInterval;
        _refusalFaultDelay = refusalFaultDelay ?? DefaultRefusalFaultDelay;
        _refusalFaultRepeat = refusalFaultRepeat ?? DefaultRefusalFaultRepeat;
        _claims = claims;
        _inputLog = inputLog;
        _firstInputUtc = firstInputUtc;
        _childStartUtc = childStartUtc;
        _resumeLaunch = resumeLaunch;
        _knownTranscriptPath = string.IsNullOrWhiteSpace(knownTranscriptPath) ? null : knownTranscriptPath;
        _sessionsRoot = string.IsNullOrWhiteSpace(sessionsRoot) ? ResolveSessionsRoot(null) : sessionsRoot!;
        _onBound = onBound;
        _onUnbound = onUnbound;
    }

    /// <summary>The rollout currently being tailed, or null while unbound (tests/diagnostics).</summary>
    public string? BoundTranscriptPath { get; private set; }

    /// <inheritdoc />
    public string? BindHow { get; private set; }

    private bool InputDelivered => _firstInputUtc is not null || _inputLog is { IsEmpty: false };

    /// <summary>
    /// Where Codex keeps its rollouts: <c>{CODEX_HOME}/sessions</c>, defaulting to
    /// <c>~/.codex/sessions</c>. CODEX_HOME resolves from the launch env first — that is the
    /// environment the CHILD actually sees — then this process's own, then the default.
    /// </summary>
    public static string ResolveSessionsRoot(IReadOnlyDictionary<string, string>? launchEnv)
    {
        string? codexHome = null;
        launchEnv?.TryGetValue("CODEX_HOME", out codexHome);
        if (string.IsNullOrWhiteSpace(codexHome))
            codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
            codexHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        return Path.Combine(codexHome, "sessions");
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void NotifyChildExited() => _childExitedAtUtc ??= DateTime.UtcNow;

    public void NotifyClaimRevoked(string path, Guid newOwner)
    {
        if (BoundTranscriptPath is not { } bound)
            return;
        try
        {
            if (!string.Equals(Path.GetFullPath(bound), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                return;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        _claimRevokedBy = newOwner;
        _claimRevoked = true;
    }

    public RunnerTranscriptDto Snapshot()
    {
        lock (_gate)
            return new RunnerTranscriptDto(_sessionId, _entries.ToArray(), _seq);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
            var path = await LocateAsync(ct);
            if (path is null)
                return; // cancelled, or the session ended without an identifiable rollout

            _logger.LogInformation(
                "Tailing Codex rollout {Path} for session {SessionId}", path, _sessionId);

            long offset = 0;
            var pending = new List<byte>();
            var dropped = false;

            while (!ct.IsCancellationRequested)
            {
                if (_claimRevoked)
                {
                    HandleClaimRevoked(path);
                    dropped = true;
                    break;
                }
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > offset)
                    {
                        byte[] buffer;
                        int read;
                        // Codex keeps the rollout open for the session's lifetime; share everything.
                        await using (var fs = new FileStream(
                            path, FileMode.Open, FileAccess.Read,
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
                catch (IOException)
                {
                    // Mid-write / transiently locked — retry on the next poll.
                }

                if (_childExitedAtUtc is { } exitedAt && DateTime.UtcNow - exitedAt >= ChildExitSettle)
                    return;

                await Task.Delay(_pollInterval, ct);
            }

            if (!dropped)
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex transcript tailer failed for session {SessionId}", _sessionId);
        }
    }

    private void HandleClaimRevoked(string path)
    {
        var newOwner = _claimRevokedBy;
        _claimRevoked = false;
        BoundTranscriptPath = null;
        BindHow = null;
        _knownTranscriptPath = null;
        _logger.LogWarning(
            "Session {SessionId}: Codex rollout {Path} was reclaimed by its namesake session {NewOwner}; "
            + "it was never ours. Dropping it and resuming discovery.",
            _sessionId, path, newOwner);
        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.ClaimRevoked,
                $"Reclaimed by namesake session {newOwner:D}",
                CandidatePath: path,
                UnboundSeconds: 0,
                Repeat: 1));
        try { _onUnbound?.Invoke(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Clearing the Codex binding for session {SessionId} failed", _sessionId); }
    }

    // Split the accumulated bytes on '\n' (never part of a UTF-8 multi-byte sequence), normalize
    // each complete line, and keep the trailing partial line for the next read — a half-written
    // row while Codex appends is normal, not an error.
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
            _logger.LogDebug(ex, "Failed to normalize Codex rollout row for session {SessionId}", _sessionId);
            return;
        }

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

    /// <summary>
    /// Finds this session's rollout, or returns null when the session ends without one. Codex
    /// creates the file lazily at the first submit (measured), so a missing rollout is the normal
    /// state and this polls for the session's lifetime.
    /// </summary>
    private async Task<string?> LocateAsync(CancellationToken ct)
    {
        DateTime? refusingSince = null;
        DateTime? emptySince = null;
        DateTime? lastFault = null;
        var faultRepeat = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. Sidecar: this runner already knew which rollout this session reads. A restart
                //    re-tails it directly — no discovery, so a busier stranger in the same cwd is
                //    never even considered.
                if (_knownTranscriptPath is { } known
                    && File.Exists(known)
                    && TryBind(known, TranscriptBindMethods.Sidecar))
                {
                    return known;
                }

                if (Directory.Exists(_sessionsRoot))
                {
                    var verdict = Evaluate();
                    if (verdict.Winner is { } winner && TryBind(winner, TranscriptBindMethods.Discovery))
                    {
                        _logger.LogInformation(
                            "Session {SessionId}: adopted Codex rollout {Path} — cwd {Cwd} matched and a "
                            + "recorded prompt is text this session was sent (C1-C4)",
                            _sessionId, winner, _cwd);
                        return winner;
                    }

                    var inputDelivered = InputDelivered;
                    refusingSince = inputDelivered && verdict.Refusals.Count > 0
                        ? refusingSince ?? DateTime.UtcNow
                        : null;
                    MaybeReportRefusal(verdict, ref refusingSince, ref lastFault, ref faultRepeat);

                    emptySince = inputDelivered && IsEmptyCensus(verdict)
                        ? emptySince ?? DateTime.UtcNow
                        : null;
                    MaybeReportNoCandidates(verdict, ref emptySince, ref lastFault, ref faultRepeat);

                    // The repeat counter belongs to the EPISODE, not the session: once neither
                    // shape is live any more the next fault starts again at 1.
                    if (refusingSince is null && emptySince is null)
                    {
                        faultRepeat = 0;
                        lastFault = null;
                    }
                }
                else
                {
                    // Not a fault on its own: CODEX_HOME/sessions does not exist until the first
                    // Codex session on this machine ever writes a rollout.
                    emptySince = null;
                    refusingSince = null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Codex rollout discovery pass failed for session {SessionId}", _sessionId);
            }

            if (_childExitedAtUtc is { } exitedAt && DateTime.UtcNow - exitedAt >= ChildExitSettle)
            {
                ReportMissingAfterChildExit();
                return null;
            }

            try { await Task.Delay(_locatePollInterval, ct); }
            catch (OperationCanceledException) { return null; }
        }

        return null;
    }

    /// <summary>
    /// Takes ownership of a rollout and records the binding. The claim (rule C1) is the atomic
    /// arbiter: candidates are evaluated without side effects and only the winner is claimed, so
    /// two tailers that both judge one file eligible cannot both adopt it.
    /// </summary>
    private bool TryBind(string path, string how)
    {
        if (_claims is not null && !_claims.TryClaim(path, _sessionId).Claimed)
        {
            _logger.LogDebug(
                "Session {SessionId}: Codex rollout {Path} is already claimed by another session; not adopting",
                _sessionId, path);
            return false;
        }

        BoundTranscriptPath = path;
        BindHow = how;
        try { _onBound?.Invoke(path, how); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Recording the transcript binding for session {SessionId} failed", _sessionId);
        }

        // Unlike Claude, EVERY Codex bind that is not a sidecar re-tail is a discovery bind — there
        // is no exact-id fast path to be quiet about, so the audit trail always fires.
        if (how != TranscriptBindMethods.Sidecar)
        {
            _events.Publish(
                SessionRunnerEventNames.SessionTranscriptBound,
                new RunnerTranscriptBoundEvent(_sessionId, path, how));
        }

        return true;
    }

    private readonly record struct CandidateRefusal(string Detail, bool IsC3, DateTime LastWriteUtc);

    private readonly record struct CandidateVerdict(
        string? Winner,
        IReadOnlyList<CandidateRefusal> Refusals,
        int FilesUnderRoot,
        int CwdMatched,
        int PostStartCandidates);

    /// <summary>
    /// Applies C1-C4 to every rollout under <c>CODEX_HOME/sessions</c> and returns the best
    /// qualifying candidate (newest mtime as a TIEBREAK only — recency is never evidence), plus the
    /// reasons any near-miss was refused, for the fault report.
    ///
    /// <para>There is deliberately NO cheap file-timestamp pre-filter in front of this, tempting as
    /// one is when the root holds every Codex session ever run on the machine. A pre-filter that
    /// drops a rollout for being older than the child answers the same question C3 does, but
    /// SILENTLY — the operator would get "0 cwd-matched" instead of a refusal naming the file and
    /// the reason, which is the diagnostic CARD-0073 exists to preserve. C2 is the cheap gate, the
    /// same way it is in the Claude tailer, and a non-matching file is never read past its lead.</para>
    /// </summary>
    private CandidateVerdict Evaluate()
    {
        var qualified = new List<(string Path, DateTime Mtime)>();
        var refusals = new List<CandidateRefusal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesUnderRoot = 0;
        var cwdMatched = 0;
        var postStartCandidates = 0;

        foreach (var file in Directory.EnumerateFiles(_sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories))
        {
            filesUnderRoot++;

            // C1 — someone else's rollout, definitively.
            if (_claims?.IsClaimedByOther(file, _sessionId) == true)
                continue;

            seen.Add(file);

            DateTime mtime;
            try { mtime = File.GetLastWriteTimeUtc(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            // C2 — a different cwd is a different session entirely; not a near-miss worth
            // reporting. Also the cheap gate: a non-matching file is never read past its lead.
            if (ProbeCwdMatchingCandidate(file) is not { } probe)
                continue;
            cwdMatched++;

            // C3 — a rollout for this session cannot have started before the session did.
            if (!EpochOk(probe.FirstTimestamp))
            {
                refusals.Add(new(
                    $"{file}: first timestamped record {probe.FirstTimestamp:O} predates the child start {_childStartUtc:O}",
                    IsC3: true,
                    mtime));
                continue;
            }

            postStartCandidates++;

            // C4 — the only positive identification: text WE sent, recorded as a prompt in there.
            if (!probe.ContentMatched)
            {
                refusals.Add(new(
                    $"{file}: no prompt in it matches input delivered to this session "
                    + $"(originator '{probe.Originator ?? "?"}', rollout session {probe.RolloutSessionId ?? "?"})",
                    IsC3: false,
                    mtime));
                continue;
            }

            qualified.Add((file, mtime));
        }

        PruneProbes(seen);

        return new CandidateVerdict(
            qualified.Count > 0 ? qualified.OrderByDescending(c => c.Mtime).First().Path : null,
            refusals,
            filesUnderRoot,
            cwdMatched,
            postStartCandidates);
    }

    /// <summary>
    /// Reads a candidate in two phases and returns it only if its recorded cwd is ours (C2).
    /// Phase one reads just enough to clear the fat <c>session_meta</c> record and find the cwd;
    /// only a cwd match earns the deep scan that collects prompts for C4.
    /// </summary>
    private CodexRolloutProbe? ProbeCwdMatchingCandidate(string file)
    {
        if (!_probes.TryGetValue(file, out var probe))
            _probes[file] = probe = new CodexRolloutProbe(file);

        if (probe.Cwd is null && !probe.Refresh(_inputLog, CodexRolloutProbe.LeadScanBytes))
            return null;
        if (!CwdMatches(probe.Cwd))
            return null;

        return probe.Refresh(_inputLog) && probe.HasRecords ? probe : null;
    }

    // Candidate state is per file and CODEX_HOME is shared with every other session on the box;
    // drop probes for files no longer under consideration so a long-lived unbound session cannot
    // grow one entry per rollout ever written on this machine.
    private void PruneProbes(HashSet<string> seen)
    {
        foreach (var stale in _probes.Keys.Where(p => !seen.Contains(p)).ToList())
            _probes.Remove(stale);
    }

    /// <summary>
    /// Rule C3. A candidate whose first TIMESTAMPED record predates the child process cannot be
    /// this session's rollout. Waived entirely for a resume launch, whose copied history carries
    /// the ORIGINAL timestamps.
    /// </summary>
    private bool EpochOk(DateTimeOffset? firstTimestamp)
    {
        if (_resumeLaunch || _childStartUtc is not { } childStart || firstTimestamp is not { } first)
            return true;
        return first.UtcDateTime >= childStart - EpochSkewSlack;
    }

    private bool CwdMatches(string? candidateCwd)
    {
        if (string.IsNullOrWhiteSpace(candidateCwd) || string.IsNullOrWhiteSpace(_cwd))
            return false;
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(candidateCwd), Path.GetFullPath(_cwd), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Candidates exist in this cwd but none proved to belong to this session. That is the SAFE
    /// outcome — the alternative is reading a stranger's conversation — but it is never silent.
    /// </summary>
    private void MaybeReportRefusal(
        CandidateVerdict verdict,
        ref DateTime? refusingSince,
        ref DateTime? lastFault,
        ref int faultRepeat)
    {
        if (verdict.Refusals.Count == 0 || refusingSince is not { } since)
            return;

        var now = DateTime.UtcNow;
        if (now - since < _refusalFaultDelay)
            return;
        if (lastFault is { } last && now - last < _refusalFaultRepeat)
            return;

        lastFault = now;
        var unbound = (now - since).TotalSeconds;
        var repeat = ++faultRepeat;
        var refusals = OrderRefusals(verdict);
        var missing = verdict.PostStartCandidates == 0
            && verdict.Refusals.Count > 0
            && verdict.Refusals.All(r => r.IsC3);
        var detail = missing
            ? $"No Codex rollout has been written for this session in the {unbound:F0}s since input was delivered; "
                + $"{verdict.Refusals.Count(r => r.IsC3)} cwd-matched rollout(s) older than the child were refused (C3) "
                + $"({FormatCensus(verdict)})"
            : $"{string.Join("; ", refusals.Take(5).Select(r => r.Detail))} ({FormatCensus(verdict)})";
        _logger.LogWarning(
            "Session {SessionId}: {Fault} in {Cwd} after {Seconds:F0}s "
            + "(report #{Repeat}) — {Detail}. "
            + "Running WITHOUT a transcript rather than binding to a session that may not be ours.",
            _sessionId, missing ? "no Codex rollout was written" : "refusing every Codex rollout candidate",
            _cwd, unbound, repeat, detail);

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                missing ? TranscriptFaultKinds.TranscriptMissing : TranscriptFaultKinds.AdoptionRefused,
                detail,
                !missing && refusals.Count == 1 ? refusals[0].Detail : null,
                unbound,
                repeat));
    }

    private static IReadOnlyList<CandidateRefusal> OrderRefusals(CandidateVerdict verdict) =>
        verdict.Refusals
            .OrderBy(r => r.IsC3)
            .ThenByDescending(r => r.LastWriteUtc)
            .ToArray();

    /// <summary>
    /// C2 found nothing at all — not "candidates existed and all were refused". A live child that
    /// has been typed at and still has zero cwd-matching rollouts is the same hole CARD-0073 closed
    /// for Claude: the correct bind outcome, but it must not be invisible.
    /// </summary>
    private void MaybeReportNoCandidates(
        CandidateVerdict verdict,
        ref DateTime? emptySince,
        ref DateTime? lastFault,
        ref int faultRepeat)
    {
        if (!IsEmptyCensus(verdict) || emptySince is not { } since)
            return;

        var now = DateTime.UtcNow;
        if (now - since < _refusalFaultDelay)
            return;
        if (lastFault is { } last && now - last < _refusalFaultRepeat)
            return;

        lastFault = now;
        var unbound = (now - since).TotalSeconds;
        var repeat = ++faultRepeat;
        var detail =
            $"No cwd-matching Codex rollout candidates after {unbound:F0}s "
            + $"({FormatCensus(verdict)}). Running WITHOUT a transcript.";
        _logger.LogWarning(
            "Session {SessionId}: no cwd-matching Codex rollout candidates in {Cwd} after {Seconds:F0}s "
            + "(report #{Repeat}) — {Census}. "
            + "Running WITHOUT a transcript; ingestion, working/idle and turn-end settlement are all "
            + "dead for this session until one appears.",
            _sessionId, _cwd, unbound, repeat, FormatCensus(verdict));

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.TranscriptMissing,
                detail,
                null,
                unbound,
                repeat));
    }

    // Input must already have been delivered: the rollout is created lazily at the first submit,
    // so an untouched composer with no file is the normal first-prompt wait, not a fault.
    private bool IsEmptyCensus(CandidateVerdict verdict) =>
        verdict.Winner is null
        && verdict.Refusals.Count == 0
        && verdict.CwdMatched == 0
        && InputDelivered
        && _childExitedAtUtc is null;

    private static string FormatCensus(CandidateVerdict verdict) =>
        $"{verdict.FilesUnderRoot} rollout(s) under the Codex sessions root, "
        + $"{verdict.CwdMatched} cwd-matched, {verdict.Refusals.Count} refused";

    // The child is dead and nothing ever bound. Only a fault if input was actually delivered: a
    // session that was never typed at legitimately never creates a rollout.
    private void ReportMissingAfterChildExit()
    {
        if (!InputDelivered)
            return;

        _logger.LogWarning(
            "Session {SessionId}: the child exited without ever producing a Codex rollout we could "
            + "identify under {Root}, although input was delivered to it. Nothing was ingested.",
            _sessionId, _sessionsRoot);

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.TranscriptMissing,
                $"The session's child process exited without producing an identifiable Codex rollout "
                + $"under {_sessionsRoot}, although input had been delivered to it.",
                null));
    }

    public async ValueTask DisposeAsync()
    {
        try { await _cts.CancelAsync(); } catch (ObjectDisposedException) { }
        if (_loop is not null)
        {
            try { await _loop; }
            catch { /* loop already logged */ }
        }
        _claims?.ReleaseAll(_sessionId);
        _cts.Dispose();
    }
}

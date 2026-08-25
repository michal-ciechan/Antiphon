using System.Text;
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
/// instead (observed 2026-07-22). So the tailer also discovers the real file. What it may NOT do is
/// guess: Claude's transcript root is per-cwd, several agents and the human operator share one
/// checkout, and on 2026-08-09 "same cwd, written recently" bound an agent to the operator's own
/// conversation — 65 of the operator's file edits were reported as the agent's work, and a
/// channel-bound agent in that state relays a stranger's turns to Telegram (CARD-0006).
///
/// So every non-exact binding must carry positive evidence that the file belongs to THIS session:
/// <list type="bullet">
/// <item><b>C1</b> — no other live session has claimed it (<see cref="TranscriptClaimRegistry"/>).</item>
/// <item><b>C2</b> — its recorded <c>cwd</c> is this session's cwd.</item>
/// <item><b>C2b</b> — it carries no CONFLICTING <c>agentName</c> record (absence stays neutral).</item>
/// <item><b>C3</b> — its first timestamped record is not older than the child process
///   (waived on a <c>--resume</c> launch, whose copied history legitimately predates the relaunch).</item>
/// <item><b>C4</b> — some user prompt in it is text this session actually received
///   (<see cref="SessionInputLog"/>). This is the only positive identification available, and it is
///   mandatory for every heuristic bind.</item>
/// </list>
/// Nothing qualifying means the session runs WITHOUT a transcript and raises a visible fault —
/// never a bind on cwd and recency alone.
/// </summary>
internal sealed class TranscriptTailer : ITranscriptTailer
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LocatePollInterval = TimeSpan.FromMilliseconds(250);

    // ~30s of consecutive faults before the first report, then roughly every 2 minutes.
    private const int LocateFaultFirstReportPolls = 120;
    private const int LocateFaultRepeatPolls = 480;
    private static readonly TimeSpan DefaultForkScanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultRefusalFaultDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultRefusalFaultRepeat = TimeSpan.FromMinutes(5);
    // A dead child writes no more transcript; give the last flush a moment, then stop looking.
    private static readonly TimeSpan ChildExitSettle = TimeSpan.FromSeconds(3);
    private const int MaxReadChunkBytes = 1 << 20; // 1 MiB per poll
    // Slack on rule C3 for clock skew between the process-start stamp and Claude's own timestamps.
    private static readonly TimeSpan EpochSkewSlack = TimeSpan.FromSeconds(2);

    private readonly Guid _sessionId;
    private readonly string _cwd;
    private readonly SessionRunnerEventHub _events;
    private readonly ILogger _logger;
    // How long to wait for the exact <session-id>.jsonl before falling back to cwd-based discovery.
    private readonly TimeSpan _exactIdGrace;

    private readonly TimeSpan _refusalFaultDelay;
    // CARD-0101: injectable purely so a test can observe the SECOND and THIRD report of one
    // continuous fault (the repeat counter and the growing unbound clock are what the server
    // escalates on). Production stays on the measured 5-minute cadence.
    private readonly TimeSpan _refusalFaultRepeat;
    private readonly TranscriptClaimRegistry? _claims;
    private readonly SessionInputLog? _inputLog;
    private readonly DateTime? _firstInputUtc;
    private readonly DateTime? _childStartUtc;
    private readonly string? _agentName;
    private readonly bool _resumeLaunch;
    private string? _knownTranscriptPath;
    private readonly Action<string, string>? _onBound;
    private readonly Action? _onUnbound;
    private readonly IKnownSessionProbe? _knownSessions;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _claimRevoked;
    private Guid _claimRevokedBy;
    private Guid? _lastExactLossHolder;
    private DateTime? _lastExactLossReport;
    private readonly object _gate = new();
    private readonly List<RunnerTranscriptEvent> _entries = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly Dictionary<string, TranscriptCandidateProbe> _probes = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;
    private long _seq;
    private DateTime? _childExitedAtUtc;

    /// <param name="claims">Process-wide "who is tailing what" registry (rule C1). Null disables the check.</param>
    /// <param name="inputLog">What this session was sent — the evidence for rule C4. Null means no
    /// heuristic bind can ever be justified, which is the safe default for a session with no input record.</param>
    /// <param name="firstInputUtc">Persisted first delivered-input time from the sidecar after runner adoption.</param>
    /// <param name="childStartUtc">Child process start, the epoch for rule C3.</param>
    /// <param name="agentName">Launch <c>--name</c>, for the C2b reject filter.</param>
    /// <param name="resumeLaunch">True for <c>--resume</c>/<c>--continue</c>: waives C3.</param>
    /// <param name="knownTranscriptPath">Sidecar-recorded path (restart re-adopt): re-tailed directly, no discovery.</param>
    /// <param name="onBound">Called with (path, how) whenever a transcript is bound, so the sidecar can record it.</param>
    /// <param name="onUnbound">Called when a bound transcript is dropped because its namesake reclaimed it.</param>
    /// <param name="knownSessions">C0: sessions this runner launched. Null disables C0.</param>
    public TranscriptTailer(
        Guid sessionId,
        string cwd,
        SessionRunnerEventHub events,
        ILogger logger,
        TimeSpan? exactIdGrace = null,
        TimeSpan? forkScanInterval = null,
        TranscriptClaimRegistry? claims = null,
        SessionInputLog? inputLog = null,
        DateTime? firstInputUtc = null,
        DateTime? childStartUtc = null,
        string? agentName = null,
        bool resumeLaunch = false,
        string? knownTranscriptPath = null,
        Action<string, string>? onBound = null,
        Action? onUnbound = null,
        IKnownSessionProbe? knownSessions = null,
        TimeSpan? refusalFaultDelay = null,
        TimeSpan? refusalFaultRepeat = null)
    {
        _sessionId = sessionId;
        _cwd = cwd;
        _events = events;
        _logger = logger;
        _exactIdGrace = exactIdGrace ?? TimeSpan.FromSeconds(10);
        _refusalFaultDelay = refusalFaultDelay ?? DefaultRefusalFaultDelay;
        _refusalFaultRepeat = refusalFaultRepeat ?? DefaultRefusalFaultRepeat;
        _claims = claims;
        _inputLog = inputLog;
        _firstInputUtc = firstInputUtc;
        _childStartUtc = childStartUtc;
        _agentName = string.IsNullOrWhiteSpace(agentName) ? null : agentName;
        _resumeLaunch = resumeLaunch;
        _knownTranscriptPath = string.IsNullOrWhiteSpace(knownTranscriptPath) ? null : knownTranscriptPath;
        _onBound = onBound;
        _onUnbound = onUnbound;
        _knownSessions = knownSessions;
        ForkScanInterval = forkScanInterval ?? DefaultForkScanInterval;
    }

    /// <summary>How often the tailer looks for a mid-session conversation fork (see RunAsync).</summary>
    private TimeSpan ForkScanInterval { get; }

    /// <summary>The transcript currently being tailed, or null while unbound (tests/diagnostics).</summary>
    public string? BoundTranscriptPath { get; private set; }

    /// <inheritdoc />
    public string? BindHow { get; private set; }

    private bool InputDelivered => _firstInputUtc is not null || _inputLog is { IsEmpty: false };

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    /// <summary>
    /// The child process is gone. A dead child writes no further transcript, so adoption attempts
    /// stop — and if input WAS delivered and nothing ever bound, that is reported now rather than
    /// polling silently for the rest of the runner's life.
    /// </summary>
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
            while (!ct.IsCancellationRequested)
            {
                var path = await LocateAsync(ct);
                if (path is null)
                    return; // cancelled, or the session ended without ever producing a transcript

                _logger.LogInformation("Tailing transcript {Path} for session {SessionId}", path, _sessionId);

                long offset = 0;
                var pending = new List<byte>();
                var lastForkScan = DateTime.UtcNow;
                var dropped = false;
                while (!ct.IsCancellationRequested)
                {
                    if (_claimRevoked)
                    {
                        HandleClaimRevoked(path);
                        dropped = true;
                        break;
                    }
                // Mid-session fork watch: /clear forks the conversation to a FRESH file (canary:
                // ClaudeLocalCommandCanaryTests) — the current file goes quiet and all further
                // activity lands in the fork. Without following it, transcript ingestion (and with
                // it working/idle + channel reply dispatch) silently dies for the rest of the
                // session (live miss 2026-07-31: the AZ Care reply after a /clear never reached
                // Telegram). Sequences stay monotonic across the switch; re-reading the fork from
                // offset 0 is safe — the server dedupes by line uuid.
                if (DateTime.UtcNow - lastForkScan >= ForkScanInterval)
                {
                    lastForkScan = DateTime.UtcNow;
                    if (TryFindNewerFork(path) is { } fork && TryBind(fork, TranscriptBindMethods.Fork))
                    {
                        _logger.LogWarning(
                            "Session {SessionId}: conversation forked mid-session (e.g. /clear); "
                            + "switching tail {Old} -> {New}", _sessionId, path, fork);
                        path = fork;
                        offset = 0;
                        pending.Clear();
                    }
                }

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

                if (!dropped)
                    return;
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

    private void HandleClaimRevoked(string path)
    {
        var newOwner = _claimRevokedBy;
        _claimRevoked = false;
        BoundTranscriptPath = null;
        BindHow = null;
        _knownTranscriptPath = null;
        _logger.LogWarning(
            "Session {SessionId}: transcript {Path} was reclaimed by its namesake session {NewOwner}; "
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
        catch (Exception ex) { _logger.LogDebug(ex, "Clearing the transcript binding for session {SessionId} failed", _sessionId); }
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
                    p.Role, p.Text, p.ToolName, p.ToolInput, p.ToolUseId, p.ToolIsError, p.StopReason,
                    p.ApiCallId, p.InputTokens, p.OutputTokens, p.CacheReadTokens, p.CacheCreationTokens,
                    p.IsApiError, p.ApiErrorClass, p.ApiErrorStatus, p.Model, p.ModelCalls);
                _entries.Add(evt);
            }
            _events.Publish(SessionRunnerEventNames.SessionTranscript, evt);
        }
    }

    /// <summary>
    /// Finds the session's JSONL, or returns null when the session ends without one. Claude creates
    /// the file lazily — for an interactive session not until the first prompt is submitted — so a
    /// missing transcript is normal and this polls for the session's lifetime. The order below is
    /// the decision procedure; each step is a positive identification, never a guess.
    /// </summary>
    private async Task<string?> LocateAsync(CancellationToken ct)
    {
        var fileName = _sessionId.ToString("D") + ".jsonl";
        var projectsRoot = ResolveProjectsRoot();

        // Consecutive polls that could not even look. A transcript that simply has not been written
        // yet is normal and stays silent; a root that is missing or unreadable is a fault, and
        // without this the session would spend its entire life failing to ingest with NOTHING in
        // the log to say so.
        var faultPolls = 0;
        DateTime? refusingSince = null;
        DateTime? emptySince = null;
        DateTime? lastRefusalFault = null;
        // CARD-0101: how many times this CONTINUOUS fault has been reported. Reset with the fault
        // itself (below), so it is a measure of this episode and never of the session's lifetime.
        var refusalRepeat = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. Sidecar: this runner already knew which file this session reads. A restart
                //    re-tails it directly — no discovery, so a busier stranger in the same cwd is
                //    never even considered (this REPLACES the old active-pre-existing heuristic).
                if (_knownTranscriptPath is { } known
                    && File.Exists(known)
                    && TryBind(known, TranscriptBindMethods.Sidecar))
                {
                    return known;
                }

                if (!Directory.Exists(projectsRoot))
                {
                    ReportRootFault(++faultPolls, projectsRoot, "the transcript root does not exist", null);
                }
                else
                {
                    faultPolls = 0;

                    // 2. Claude honoured --session-id: the filename IS the positive identification.
                    foreach (var dir in Directory.EnumerateDirectories(projectsRoot))
                    {
                        var candidate = Path.Combine(dir, fileName);
                        if (File.Exists(candidate) && TryBind(candidate, TranscriptBindMethods.Exact))
                            return candidate;
                    }

                    // 3. Claude forked to a self-chosen id. Discover by evidence, once the exact
                    //    file has had a fair chance to appear.
                    if (DateTime.UtcNow - _startedAtUtc >= _exactIdGrace)
                    {
                        var verdict = EvaluateCandidates(projectsRoot);
                        if (verdict.Winner is { } winner && TryBind(winner, verdict.How))
                        {
                            _logger.LogWarning(
                                "Session {SessionId}: <session-id>.jsonl never appeared (Claude forked the id); "
                                + "adopting {Path} ({How}) — cwd {Cwd}",
                                _sessionId, winner, verdict.How, _cwd);
                            return winner;
                        }

                        var inputDelivered = InputDelivered;
                        refusingSince = verdict.ExactFileHeldBy is not null
                            || (inputDelivered && verdict.Refusals.Count > 0)
                            ? refusingSince ?? DateTime.UtcNow
                            : null;
                        MaybeReportRefusal(verdict, ref refusingSince, ref lastRefusalFault, ref refusalRepeat);

                        // CARD-0073 S1: C2 itself found nothing, so Refusals is empty and the
                        // refusal path stays silent. A live child that has already been typed at
                        // and still has zero cwd-matching candidates is the 10e30ff7 shape — it
                        // must not run unbound forever with nothing on the wire.
                        emptySince = inputDelivered && IsEmptyCensus(verdict)
                            ? emptySince ?? DateTime.UtcNow
                            : null;
                        MaybeReportNoCandidates(verdict, ref emptySince, ref lastRefusalFault, ref refusalRepeat);

                        // The repeat counter belongs to the EPISODE, not the session: once neither
                        // shape is live any more the next fault starts again at 1, so an escalation
                        // can never be inherited from a fault that has already cleared.
                        if (refusingSince is null && emptySince is null)
                        {
                            refusalRepeat = 0;
                            lastRefusalFault = null;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Retry — a directory being written to can throw transiently. But a fault that
                // never clears used to be invisible, so it is reported once it persists.
                ReportRootFault(++faultPolls, projectsRoot, ex.Message, ex);
            }

            if (_childExitedAtUtc is { } exitedAt && DateTime.UtcNow - exitedAt >= ChildExitSettle)
            {
                ReportMissingAfterChildExit();
                return null;
            }

            try { await Task.Delay(LocatePollInterval, ct); }
            catch (OperationCanceledException) { return null; }
        }

        return null;
    }

    /// <summary>
    /// Takes ownership of a transcript and records the binding. The claim (rule C1) is the atomic
    /// arbiter: candidates are evaluated without side effects, and only the winner is claimed, so
    /// two tailers that both judge one file eligible cannot both adopt it — exactly one
    /// <see cref="TranscriptClaimRegistry.TryClaim"/> succeeds and the loser simply keeps looking.
    /// </summary>
    private bool TryBind(string path, string how)
    {
        if (_claims is not null)
        {
            var claim = _claims.TryClaim(path, _sessionId);
            if (!claim.Claimed)
            {
                if (how == TranscriptBindMethods.Exact)
                {
                    var holder = _claims.OwnerOf(path);
                    _lastExactLossHolder = holder?.Owner;
                    if (_lastExactLossReport is null
                        || DateTime.UtcNow - _lastExactLossReport.Value >= _refusalFaultRepeat)
                    {
                        _lastExactLossReport = DateTime.UtcNow;
                        _logger.LogWarning(
                            "Session {SessionId}: its own transcript {Path} is held by session {Holder} ({Strength}); not adopting.",
                            _sessionId, path, holder?.Owner, holder?.Strength);
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Session {SessionId}: transcript {Path} is already claimed by another session; not adopting",
                        _sessionId, path);
                }

                return false;
            }
        }

        BoundTranscriptPath = path;
        BindHow = how;
        try { _onBound?.Invoke(path, how); }
        catch (Exception ex) { _logger.LogDebug(ex, "Recording the transcript binding for session {SessionId} failed", _sessionId); }

        // Exact-id and sidecar binds are self-evidently this session's file; the heuristic ones are
        // the interesting audit trail (which file is an agent actually reading, and on what basis).
        if (how is not TranscriptBindMethods.Exact and not TranscriptBindMethods.Sidecar)
        {
            _events.Publish(
                SessionRunnerEventNames.SessionTranscriptBound,
                new RunnerTranscriptBoundEvent(_sessionId, path, how));
        }

        return true;
    }

    private readonly record struct CandidateVerdict(
        string? Winner,
        string How,
        IReadOnlyList<string> Refusals,
        int FilesUnderRoot,
        int CwdMatched,
        string? ExactFileHeldBy = null);

    /// <summary>
    /// Applies C1–C4 to every transcript under the projects root and returns the best qualifying
    /// candidate (newest mtime as a TIEBREAK only — recency is never evidence), plus the reasons
    /// any near-miss was refused, for the fault report.
    /// </summary>
    private CandidateVerdict EvaluateCandidates(string projectsRoot)
    {
        var qualified = new List<(string Path, DateTime Mtime)>();
        var refusals = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesUnderRoot = 0;
        var cwdMatched = 0;

        foreach (var file in Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            seen.Add(file);
            filesUnderRoot++;

            if (Path.GetFileNameWithoutExtension(file).Equals(_sessionId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                continue; // the exact file is handled by the fast path

            // C0 — CARD-0181. A transcript whose basename is another session's id is that session's
            // by name, and this runner knows the session exists because it wrote a sidecar for it.
            // No amount of content evidence makes it ours: two incarnations of one always-on agent
            // share cwd, --name and a templated launch note, so C2/C2b/C4 all pass — and C3 only
            // rejects OLDER files.
            if (TranscriptClaimRegistry.TryReadNamesake(file) is { } namesake
                && namesake != _sessionId
                && _knownSessions?.Exists(namesake) == true)
            {
                refusals.Add($"{file}: named for session {namesake:D}, which this runner launched");
                continue;
            }

            // C1 — someone else's transcript, definitively.
            if (_claims?.IsClaimedByOther(file, _sessionId) == true)
                continue;

            DateTime mtime;
            try { mtime = File.GetLastWriteTimeUtc(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            // C2 — a different cwd is a different conversation entirely; not a near-miss worth
            // reporting. Also the cheap gate: a non-matching file is never read past its lead.
            if (ProbeCwdMatchingCandidate(file) is not { } probe)
                continue;
            cwdMatched++;

            // C2b — a name we know is not ours. Absence is neutral: operator sessions may carry
            // their own names and older Claude versions may write none at all.
            if (_agentName is not null
                && probe.AgentName is { } candidateName
                && !string.Equals(candidateName, _agentName, StringComparison.OrdinalIgnoreCase))
            {
                refusals.Add($"{file}: agent-name '{candidateName}' is not '{_agentName}'");
                continue;
            }

            // C3 — a transcript for this session cannot have started before the session did.
            if (!EpochOk(probe.FirstTimestamp))
            {
                refusals.Add(
                    $"{file}: first timestamped record {probe.FirstTimestamp:O} predates the child start {_childStartUtc:O}");
                continue;
            }

            // C4 — the only positive identification: text WE sent, recorded as a prompt in there.
            if (!probe.ContentMatched)
            {
                refusals.Add($"{file}: no prompt in it matches input delivered to this session");
                continue;
            }

            qualified.Add((file, mtime));
        }

        PruneProbes(seen);

        if (qualified.Count > 0)
        {
            return new CandidateVerdict(
                qualified.OrderByDescending(c => c.Mtime).First().Path,
                TranscriptBindMethods.Discovery,
                refusals,
                filesUnderRoot,
                cwdMatched,
                ExactFileHeldBy: FormatHolder(_lastExactLossHolder));
        }

        return new CandidateVerdict(
            null, TranscriptBindMethods.Discovery, refusals, filesUnderRoot, cwdMatched,
            ExactFileHeldBy: FormatHolder(_lastExactLossHolder));
    }

    /// <summary>
    /// Reads a candidate in two phases, and returns it only if its recorded cwd is ours (C2).
    ///
    /// Phase one reads just enough to find the cwd — the projects root holds every project on the
    /// machine, and answering "is this even our working directory?" must not cost a full read of
    /// each one. Only a cwd match earns the deep scan that collects prompts for C4.
    /// </summary>
    private TranscriptCandidateProbe? ProbeCwdMatchingCandidate(string file)
    {
        if (!_probes.TryGetValue(file, out var probe))
            _probes[file] = probe = new TranscriptCandidateProbe(file);

        if (probe.Cwd is null && !probe.Refresh(_inputLog, TranscriptCandidateProbe.LeadScanBytes))
            return null;
        if (!CwdMatches(probe.Cwd))
            return null;

        return probe.Refresh(_inputLog) && probe.HasRecords ? probe : null;
    }

    // Candidate state is per file and the projects root is shared with every other session on the
    // box; drop probes for files that have gone away so a long-lived unbound session cannot grow
    // one entry per transcript ever written in this cwd.
    private void PruneProbes(HashSet<string> seen)
    {
        foreach (var stale in _probes.Keys.Where(p => !seen.Contains(p)).ToList())
            _probes.Remove(stale);
    }

    /// <summary>
    /// Rule C3. A candidate whose first TIMESTAMPED record predates the child process cannot be
    /// this session's transcript. Untimestamped records (the leading meta block) are not evidence
    /// either way, so a file that has produced none yet passes — C4 is what actually identifies it.
    /// Waived entirely for a resume launch: <c>--resume</c> can fork to a new file whose copied
    /// history carries the ORIGINAL timestamps, and refusing that would break resume re-adoption.
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
    /// A conversation file that forked off the one being tailed: created AFTER this tailer started,
    /// written more recently than the current file, same recorded cwd — and, since CARD-0006, also
    /// unclaimed (C1) and content-matched (C4), so a sibling session's <c>/clear</c> fork in the
    /// same cwd can never be stolen. A fresh <c>/clear</c> fork holds only the command record at
    /// first, so the switch defers until this session's next real prompt lands in it: harmless, the
    /// old file is quiet, working/idle correctly reads idle, and the queued post-clear delivery is
    /// what completes the identification.
    /// </summary>
    private string? TryFindNewerFork(string currentPath)
    {
        try
        {
            var currentWrite = File.GetLastWriteTimeUtc(currentPath);
            var projectDir = Path.GetDirectoryName(currentPath);
            if (projectDir is null || !Directory.Exists(projectDir))
                return null;

            string? best = null;
            var bestWrite = DateTime.MinValue;
            foreach (var file in Directory.EnumerateFiles(projectDir, "*.jsonl"))
            {
                if (string.Equals(file, currentPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (TranscriptClaimRegistry.TryReadNamesake(file) is { } forkNamesake
                    && forkNamesake != _sessionId
                    && _knownSessions?.Exists(forkNamesake) == true)
                    continue;
                if (_claims?.IsClaimedByOther(file, _sessionId) == true)
                    continue;
                DateTime created, written;
                try
                {
                    created = File.GetCreationTimeUtc(file);
                    written = File.GetLastWriteTimeUtc(file);
                }
                catch (IOException) { continue; }
                if (created < _startedAtUtc || written <= currentWrite || written <= bestWrite)
                    continue;

                if (ProbeCwdMatchingCandidate(file) is not { } probe)
                    continue;
                if (_agentName is not null
                    && probe.AgentName is { } candidateName
                    && !string.Equals(candidateName, _agentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!probe.ContentMatched)
                    continue;

                best = file;
                bestWrite = written;
            }
            return best;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Reports a persistent inability to look for the transcript at all. Deliberately NOT called
    /// when the transcript merely has not appeared yet — Claude creates it lazily, so for an
    /// interactive session that is the normal state for as long as the user takes to type. Only a
    /// missing or unreadable root is a fault, and at four polls a second it has to be rate-limited:
    /// once it has persisted ~30s, then every ~2 minutes.
    /// </summary>
    private void ReportRootFault(int faultPolls, string projectsRoot, string reason, Exception? ex)
    {
        var firstReport = faultPolls == LocateFaultFirstReportPolls;
        var repeatReport = faultPolls > LocateFaultFirstReportPolls
            && faultPolls % LocateFaultRepeatPolls == 0;
        if (!firstReport && !repeatReport)
            return;

        _logger.LogWarning(
            ex,
            "Session {SessionId}: cannot search for a transcript under {ProjectsRoot} after {Seconds:F0}s — {Reason}. "
            + "Ingestion, working/idle and channel replies are all dead for this session until it clears.",
            _sessionId, projectsRoot, faultPolls * LocatePollInterval.TotalSeconds, reason);

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.TranscriptMissing,
                $"Cannot search for a transcript under {projectsRoot}: {reason}",
                null));
    }

    /// <summary>
    /// Candidates exist in this cwd but none of them proved to belong to this session. That is the
    /// SAFE outcome — the alternative is reading a stranger's conversation — but it is never
    /// silent: after <see cref="_refusalFaultDelay"/> of continuous refusal it becomes an incident,
    /// repeated at most every five minutes.
    /// </summary>
    private void MaybeReportRefusal(
        CandidateVerdict verdict,
        ref DateTime? refusingSince,
        ref DateTime? lastRefusalFault,
        ref int refusalRepeat)
    {
        if ((verdict.Refusals.Count == 0 && verdict.ExactFileHeldBy is null)
            || refusingSince is not { } since)
            return;

        var now = DateTime.UtcNow;
        if (now - since < _refusalFaultDelay)
            return;
        if (lastRefusalFault is { } last && now - last < _refusalFaultRepeat)
            return;

        lastRefusalFault = now;
        var unbound = (now - since).TotalSeconds;
        var repeat = ++refusalRepeat;
        var detail = $"{string.Join("; ", verdict.Refusals.Take(5))} ({FormatCensus(verdict)})";
        _logger.LogWarning(
            "Session {SessionId}: refusing every transcript candidate in {Cwd} after {Seconds:F0}s "
            + "(report #{Repeat}) — {Detail}. "
            + "Running WITHOUT a transcript rather than binding to a conversation that may not be ours.",
            _sessionId, _cwd, unbound, repeat, detail);

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.AdoptionRefused,
                detail,
                verdict.Refusals.Count == 1 ? verdict.Refusals[0] : null,
                unbound,
                repeat));
    }

    /// <summary>
    /// C2 found nothing at all — not "candidates existed and all were refused", which
    /// <see cref="MaybeReportRefusal"/> already reports. A live child that has been typed at
    /// and still has zero cwd-matching files after <see cref="_refusalFaultDelay"/> is the
    /// CARD-0073 hole (session 10e30ff7): nothing qualifying is the correct bind outcome, but
    /// it used to be invisible. Same delay and repeat cadence as the refusal path, one knob.
    /// </summary>
    private void MaybeReportNoCandidates(
        CandidateVerdict verdict,
        ref DateTime? emptySince,
        ref DateTime? lastRefusalFault,
        ref int refusalRepeat)
    {
        if (!IsEmptyCensus(verdict) || emptySince is not { } since)
            return;

        var now = DateTime.UtcNow;
        if (now - since < _refusalFaultDelay)
            return;
        if (lastRefusalFault is { } last && now - last < _refusalFaultRepeat)
            return;

        lastRefusalFault = now;
        var unbound = (now - since).TotalSeconds;
        var repeat = ++refusalRepeat;
        var detail =
            $"No cwd-matching transcript candidates after {(now - since).TotalSeconds:F0}s "
            + $"({FormatCensus(verdict)}). Running WITHOUT a transcript.";
        _logger.LogWarning(
            "Session {SessionId}: no cwd-matching transcript candidates in {Cwd} after {Seconds:F0}s "
            + "(report #{Repeat}) — {Census}. "
            + "Running WITHOUT a transcript; ingestion, working/idle and channel replies are dead until one appears.",
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

    // Input must already have been delivered: Claude creates the file lazily, so an untouched
    // composer with an empty projects dir is the normal first-prompt wait, not a fault. Child
    // exit is ReportMissingAfterChildExit's job — do not steal it. Refusals belong to
    // MaybeReportRefusal so the two shapes stay distinguishable.
    private bool IsEmptyCensus(CandidateVerdict verdict) =>
        verdict.Winner is null
        && verdict.Refusals.Count == 0
        && verdict.CwdMatched == 0
        && verdict.ExactFileHeldBy is null
        && InputDelivered
        && _childExitedAtUtc is null;

    private static string FormatCensus(CandidateVerdict verdict)
    {
        var census =
            $"{verdict.FilesUnderRoot} file(s) under the transcript root, "
            + $"{verdict.CwdMatched} cwd-matched, {verdict.Refusals.Count} refused";
        if (verdict.ExactFileHeldBy is { } holder)
            census += $", exact file held by {holder}";
        return census;
    }

    private static string? FormatHolder(Guid? holder) => holder?.ToString("D");

    // The child is dead and nothing ever bound. Only a fault if input was actually delivered: a
    // session that was never typed at legitimately has no transcript to find.
    private void ReportMissingAfterChildExit()
    {
        if (!InputDelivered)
            return;

        _logger.LogWarning(
            "Session {SessionId}: the child exited without ever producing a transcript we could identify, "
            + "although input was delivered to it. Nothing was ingested for this session.",
            _sessionId);

        _events.Publish(
            SessionRunnerEventNames.SessionTranscriptFault,
            new RunnerTranscriptFaultEvent(
                _sessionId,
                TranscriptFaultKinds.TranscriptMissing,
                "The session's child process exited without producing an identifiable transcript, "
                + "although input had been delivered to it.",
                null));
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
        _claims?.ReleaseAll(_sessionId);
        _cts.Dispose();
    }
}

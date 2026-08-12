namespace Antiphon.SessionRunner.Contracts;

public sealed record RunnerLaunchRequest(
    Guid SessionId,
    string Exe,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string Cwd,
    int Cols,
    int Rows,
    int MemoryLimitMb = 0,
    // When true the runner tails the agent's Claude Code JSONL session transcript and emits
    // structured SessionTranscript events. Only meaningful for ClaudeCode agents.
    bool TranscriptEnabled = false);

public sealed record RunnerInputRequest(string Input);

public sealed record RunnerResizeRequest(int Cols, int Rows);

public sealed record RunnerSessionDto(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt,
    string Status,
    int? ExitCode,
    string ExitReason,
    long LastSequence,
    // Pid of the detached pty-host process owning this session's ConPTY (null pre-split/unknown).
    int? HostPid = null,
    // True when this runner re-attached to a host that survived a previous runner's death.
    bool Adopted = false);

public sealed record RunnerBufferDto(
    Guid SessionId,
    string Buffer,
    long LastSequence);

public sealed record RunnerSnapshotDto(
    Guid SessionId,
    string RawOutput,
    string RenderedScreen,
    long LastSequence,
    DateTime StartedAt);

public sealed record RunnerOutputEvent(
    Guid SessionId,
    long Sequence,
    string Text);

public sealed record RunnerSessionStartedEvent(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt);

public sealed record RunnerSessionExitedEvent(
    Guid SessionId,
    int? ExitCode,
    string ExitReason,
    long LastSequence);

/// <summary>
/// A restarted runner re-attached to a still-live pty-host (the session kept running the whole
/// time). Consumers should treat it as "still running, refresh buffers via the resync path" -
/// NOT as a fresh start.
/// </summary>
public sealed record RunnerSessionAdoptedEvent(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt,
    long LastSequence);

/// <summary>
/// One normalized entry parsed from the agent's Claude Code JSONL session transcript.
/// <see cref="Sequence"/> is monotonic per session in file order (stable across re-tails of the
/// append-only file), so consumers can order and de-duplicate on (SessionId, Sequence).
/// </summary>
public sealed record RunnerTranscriptEvent(
    Guid SessionId,
    long Sequence,
    string Kind,
    string? Uuid,
    string? ParentUuid,
    DateTimeOffset? Timestamp,
    string? Role,
    string? Text,
    string? ToolName,
    string? ToolInput,
    string? ToolUseId,
    bool? ToolIsError,
    string? StopReason,
    // API-call attribution (assistant records only). ApiCallId is the Anthropic message id; the
    // several JSONL lines of one API call share it and repeat the SAME usage numbers, so consumers
    // must group by ApiCallId and count usage once — summing per line overcounts.
    string? ApiCallId = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheCreationTokens = null);

/// <summary>Full ordered transcript snapshot for a session (used for catch-up after a missed stream).</summary>
public sealed record RunnerTranscriptDto(
    Guid SessionId,
    IReadOnlyList<RunnerTranscriptEvent> Entries,
    long LastSequence);

/// <summary>Normalized transcript entry kinds (see <see cref="RunnerTranscriptEvent.Kind"/>).</summary>
public static class TranscriptKinds
{
    public const string UserPrompt = "UserPrompt";
    public const string AssistantText = "AssistantText";
    public const string Thinking = "Thinking";
    public const string ToolCall = "ToolCall";
    public const string ToolResult = "ToolResult";
    public const string TurnTitle = "TurnTitle";
    public const string TurnEnd = "TurnEnd";

    /// <summary>
    /// A context compaction boundary (Claude Code JSONL: type=system, subtype=compact_boundary —
    /// shape pinned by ClaudeCompactionCanaryTests / Fixtures/compact-boundary.jsonl). NOT a turn
    /// end, and excluded from working/idle activity checks — compaction is normal idle-time
    /// housekeeping, not agent work.
    /// </summary>
    public const string CompactBoundary = "CompactBoundary";

    /// <summary>
    /// SERVER-SYNTHESIZED (never read from a JSONL): written when a session is relaunched and its
    /// persisted transcript still reads mid-turn — the old process died (reboot, crash, kill)
    /// before its TurnEnd could land, and nothing is running any more. Counts as a turn END for
    /// working/idle purposes, exactly like the interrupt marker: without it the relaunched
    /// session reads "working" forever and every WhenIdle delivery strands (live miss 2026-08-08,
    /// Antiphon-Opus stuck "Working" across a restart).
    /// </summary>
    public const string SessionRestartBoundary = "SessionRestartBoundary";

    /// <summary>
    /// The marker Claude Code writes into its JSONL as a USER message when a turn is aborted —
    /// "[Request interrupted by user]" (Esc) or "[Request interrupted by user for tool use]"
    /// (tool call rejected). An interrupted turn produces NO TurnEnd, so this marker IS the
    /// turn's end for working/idle purposes: treating it as activity left sessions permanently
    /// "working" and stranded every WhenIdle delivery (live miss 2026-07-29). Shape pinned
    /// against real Claude by ClaudeInterruptCanaryTests.
    /// </summary>
    public const string InterruptedPromptPrefix = "[Request interrupted";

    /// <summary>True when a transcript entry is an interrupt marker (see <see cref="InterruptedPromptPrefix"/>).</summary>
    public static bool IsInterruptPrompt(string? kind, string? text) =>
        kind == UserPrompt
        && text is not null
        && text.TrimStart().StartsWith(InterruptedPromptPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Local slash-commands (/model, /status, …) write their invocation and output into the JSONL
    /// as USER messages wrapped in these tags — and produce NO TurnEnd (no API call happens).
    /// They are housekeeping, not agent work: counting them as activity flipped the session to
    /// permanently "working" and stranded every WhenIdle delivery until the next real turn
    /// (live miss 2026-07-31: /model in the AZ Care session stranded a Telegram message).
    /// </summary>
    public const string LocalCommandPrefix = "<command-name>";
    public const string LocalCommandStdoutPrefix = "<local-command-stdout>";

    /// <summary>True when a transcript entry is a local slash-command record (see <see cref="LocalCommandPrefix"/>).</summary>
    public static bool IsLocalCommandRecord(string? kind, string? text)
    {
        if (kind != UserPrompt || text is null)
            return false;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith(LocalCommandPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(LocalCommandStdoutPrefix, StringComparison.Ordinal);
    }
}

/// <summary>
/// Exit reason strings the RUNNER synthesises (as opposed to the pty-host's PtyExitReason values,
/// which pass through verbatim). The server maps these onto its AgentExitReason enum by name, so
/// any string added here needs a matching enum member server-side to survive the parse.
/// </summary>
public static class RunnerExitReasons
{
    /// <summary>
    /// The CPU watchdog killed the session: its process was burning a core while the transcript
    /// said the turn had ended (idle TUI busy-loop, live incident 2026-08-08 — claude.exe pegged
    /// at ~110% of a core for 80+ CPU-minutes at an idle prompt). The work itself completed, so
    /// the server treats this as a clean stop and the session stays resumable.
    /// </summary>
    public const string CpuSpinKilled = "CpuSpinKilled";
}

/// <summary>
/// The runner could not safely bind a transcript to a session (CARD-0006). Emitted INSTEAD of
/// silently adopting a file that might belong to somebody else — the live miss on 2026-08-09 bound
/// an agent to the human operator's own Claude conversation on nothing more than "same cwd, written
/// recently", and the only warning was one WRN line nobody watches.
/// </summary>
public sealed record RunnerTranscriptFaultEvent(
    Guid SessionId,
    string Kind,
    string Detail,
    string? CandidatePath);

/// <summary>
/// A transcript was bound by HEURISTIC (cwd discovery / fork follow / migration shim) rather than
/// by the exact <c>&lt;session-id&gt;.jsonl</c> filename. Info-level audit: the binding passed every
/// adoption rule, but which file an agent is reading from should never be invisible.
/// </summary>
public sealed record RunnerTranscriptBoundEvent(
    Guid SessionId,
    string TranscriptPath,
    string How);

/// <summary>Values for <see cref="RunnerTranscriptFaultEvent.Kind"/>.</summary>
public static class TranscriptFaultKinds
{
    /// <summary>Candidate transcripts existed in this session's cwd but none proved it owned them.</summary>
    public const string AdoptionRefused = "AdoptionRefused";

    /// <summary>
    /// No transcript can be found at all — the projects root is missing/unreadable, or the child
    /// exited after input was delivered without ever producing one.
    /// </summary>
    public const string TranscriptMissing = "TranscriptMissing";

    /// <summary>A mid-session fork was detected but could not be attributed to this session.</summary>
    public const string ForkUnresolved = "ForkUnresolved";

    /// <summary>Two sessions were pointed at the same transcript (slice 2, SessionStart marker).</summary>
    public const string MarkerConflict = "MarkerConflict";
}

/// <summary>How a transcript came to be bound (<see cref="RunnerTranscriptBoundEvent.How"/>).</summary>
public static class TranscriptBindMethods
{
    /// <summary>Claude honoured <c>--session-id</c>: the filename itself is the positive id.</summary>
    public const string Exact = "exact";

    /// <summary>Re-tail after a runner restart, from the runner's own transcript sidecar.</summary>
    public const string Sidecar = "sidecar";

    /// <summary>Cwd discovery of an id-forked transcript, positively identified by content.</summary>
    public const string Discovery = "discovery";

    /// <summary>Mid-session fork (e.g. <c>/clear</c>) followed.</summary>
    public const string Fork = "fork";

    /// <summary>Restart re-adopt of a session that predates sidecars (see the migration shim).</summary>
    public const string MigrationShim = "migration-shim";
}

/// <summary>
/// What this runner's sessions are actually served by (<c>GET /capabilities</c>).
///
/// <para>The delivery ceilings are COUPLED to the pseudoconsole (CARD-0037): the inbox conhost
/// strips the bracketed-paste markers and clips every body at ~1 KB, the shipped modern pair
/// forwards them and takes 86 400 bytes in one write. The server sizes bodies against ONE of those
/// two envelopes, so it must be able to ASK rather than assume its own environment matches the
/// runner's — a server configured <c>modern</c> in front of an inbox runner would type bodies 48x
/// larger than that pty can carry, silently.</para>
/// </summary>
/// <param name="PtyBackend"><c>InboxConhost</c> or <c>ModernConPty</c> — the resolved value, not the request.</param>
/// <param name="PtyBackendRequested">The raw flag value that was resolved.</param>
/// <param name="PtyBackendReason">Human-readable explanation, always populated.</param>
/// <param name="PtyBackendFellBack">True when modern was asked for and the redistributable was not there.</param>
public sealed record RunnerCapabilitiesDto(
    string PtyBackend,
    string PtyBackendRequested,
    string PtyBackendReason,
    bool PtyBackendFellBack);

public static class SessionRunnerEventNames
{
    public const string SessionStarted = "SessionStarted";
    public const string SessionAdopted = "SessionAdopted";
    public const string SessionOutput = "SessionOutput";
    public const string SessionExited = "SessionExited";
    public const string SessionError = "SessionError";
    public const string SessionHeartbeat = "SessionHeartbeat";
    public const string SessionTranscript = "SessionTranscript";
    public const string SessionTranscriptFault = "SessionTranscriptFault";
    public const string SessionTranscriptBound = "SessionTranscriptBound";
}

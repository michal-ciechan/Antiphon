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

public static class SessionRunnerEventNames
{
    public const string SessionStarted = "SessionStarted";
    public const string SessionAdopted = "SessionAdopted";
    public const string SessionOutput = "SessionOutput";
    public const string SessionExited = "SessionExited";
    public const string SessionError = "SessionError";
    public const string SessionHeartbeat = "SessionHeartbeat";
    public const string SessionTranscript = "SessionTranscript";
}

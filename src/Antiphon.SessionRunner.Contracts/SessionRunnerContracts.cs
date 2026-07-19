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
    long LastSequence);

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
    string? StopReason);

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

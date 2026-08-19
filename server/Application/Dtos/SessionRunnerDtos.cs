using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Dtos;

public sealed record SessionRunnerSessionDto(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt,
    string Status,
    int? ExitCode,
    AgentExitReason ExitReason,
    long LastSequence,
    // Pid of the detached pty-host owning this session's ConPTY (null pre-split/unknown).
    int? HostPid = null,
    // True when the runner re-attached to a host that survived a previous runner's death.
    bool Adopted = false);

public sealed record SessionRunnerBufferDto(
    Guid SessionId,
    string Buffer,
    long LastSequence);

public sealed record SessionRunnerSnapshotDto(
    Guid SessionId,
    string RawOutput,
    string RenderedScreen,
    long LastSequence,
    DateTime StartedAt);

public sealed record SessionRunnerOutputEvent(
    Guid SessionId,
    long Sequence,
    string Text);

public sealed record SessionRunnerExitedEvent(
    Guid SessionId,
    int? ExitCode,
    AgentExitReason ExitReason,
    long LastSequence);

public sealed record SessionRunnerTranscriptEvent(
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
    // API-call attribution (assistant records only) — see RunnerTranscriptEvent: lines of one API
    // call share ApiCallId and repeat identical usage; group by ApiCallId, count usage once.
    string? ApiCallId = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheCreationTokens = null,
    // API-error stub evidence (CARD-0072) — see RunnerTranscriptEvent: additive-optional so a
    // lagging runner (nulls) or a lagging server (ignored members) stays compatible.
    bool? IsApiError = null,
    string? ApiErrorClass = null,
    int? ApiErrorStatus = null,
    // message.model (CARD-0082) — additive-optional, same lag-safe mix as the API-error fields.
    string? Model = null);

public sealed record SessionRunnerTranscriptDto(
    Guid SessionId,
    IReadOnlyList<SessionRunnerTranscriptEvent> Entries,
    long LastSequence);

/// <summary>A restarted runner re-attached to a surviving pty-host; the session never stopped.</summary>
public sealed record SessionRunnerAdoptedEvent(
    Guid SessionId,
    int? Pid,
    long LastSequence);

/// <summary>
/// The runner refused to bind a transcript to a session (CARD-0006) — it is running without one
/// rather than risk reading a conversation that may belong to somebody else.
/// </summary>
public sealed record SessionRunnerTranscriptFaultEvent(
    Guid SessionId,
    string Kind,
    string Detail,
    string? CandidatePath);

/// <summary>A transcript was bound by heuristic (discovery/fork/shim) rather than by exact id.</summary>
public sealed record SessionRunnerTranscriptBoundEvent(
    Guid SessionId,
    string TranscriptPath,
    string How);

public sealed record SessionRunnerEvent(
    string EventName,
    Guid SessionId,
    SessionRunnerOutputEvent? Output = null,
    SessionRunnerExitedEvent? Exited = null,
    SessionRunnerTranscriptEvent? Transcript = null,
    SessionRunnerAdoptedEvent? Adopted = null,
    SessionRunnerTranscriptFaultEvent? TranscriptFault = null,
    SessionRunnerTranscriptBoundEvent? TranscriptBound = null);

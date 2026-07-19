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
    string? StopReason);

public sealed record SessionRunnerTranscriptDto(
    Guid SessionId,
    IReadOnlyList<SessionRunnerTranscriptEvent> Entries,
    long LastSequence);

/// <summary>A restarted runner re-attached to a surviving pty-host; the session never stopped.</summary>
public sealed record SessionRunnerAdoptedEvent(
    Guid SessionId,
    int? Pid,
    long LastSequence);

public sealed record SessionRunnerEvent(
    string EventName,
    Guid SessionId,
    SessionRunnerOutputEvent? Output = null,
    SessionRunnerExitedEvent? Exited = null,
    SessionRunnerTranscriptEvent? Transcript = null,
    SessionRunnerAdoptedEvent? Adopted = null);

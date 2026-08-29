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
    bool Adopted = false,
    // CARD-0161: herdr agent_status; null for pty / older runners.
    string? AgentStatus = null,
    // CARD-0162: when AgentStatus last changed (hysteresis). Additive — older runners omit it.
    DateTime? AgentStatusSinceUtc = null,
    // CARD-0180 S4: runner transcript bind. Null on older runners / no tailer.
    bool? TranscriptBound = null,
    string? TranscriptBindHow = null,
    // CARD-0190: reason an unbound transcript is not yet available. Null on older runners.
    string? TranscriptUnboundReason = null,
    // CARD-0186 S2/S3: which lane hosts the child. Null on older runners. Values: SessionBackends.
    string? Backend = null,
    // CARD-0186 S3: HerdrPendingReasons.Unreachable while adoption is waiting on herdr.
    string? Pending = null,
    // CARD-0186 S3: stamped by the single-session GET after a passing herdr liveness verify.
    DateTime? HerdrVerifiedAtUtc = null,
    // CARD-0213: HerdrPaneOrigins. Null for pty / older runners.
    string? HerdrOrigin = null);

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
    string? Model = null,
    // Grok turn_completed.usage.modelCalls (CARD-0157) — additive-optional, same mix.
    int? ModelCalls = null);

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
/// <param name="UnboundSeconds">
/// How long the session has been CONTINUOUSLY in this fault (CARD-0101). Zero from a runner that
/// predates the field, which simply means no escalation — never a false escalation.
/// </param>
/// <param name="Repeat">1 for the first report of this continuous fault, incrementing per repeat.</param>
public sealed record SessionRunnerTranscriptFaultEvent(
    Guid SessionId,
    string Kind,
    string Detail,
    string? CandidatePath,
    double UnboundSeconds = 0,
    int Repeat = 1);

/// <summary>A transcript was bound by heuristic (discovery/fork/shim) rather than by exact id.</summary>
public sealed record SessionRunnerTranscriptBoundEvent(
    Guid SessionId,
    string TranscriptPath,
    string How);

/// <summary>
/// CARD-0162: herdr agent_status changed. PreviousAgentStatus null on first observation.
/// </summary>
public sealed record SessionRunnerAgentStatusEvent(
    Guid SessionId,
    string AgentStatus,
    string? PreviousAgentStatus,
    DateTime ObservedAtUtc);

public sealed record SessionRunnerEvent(
    string EventName,
    Guid SessionId,
    SessionRunnerOutputEvent? Output = null,
    SessionRunnerExitedEvent? Exited = null,
    SessionRunnerTranscriptEvent? Transcript = null,
    SessionRunnerAdoptedEvent? Adopted = null,
    SessionRunnerTranscriptFaultEvent? TranscriptFault = null,
    SessionRunnerTranscriptBoundEvent? TranscriptBound = null,
    SessionRunnerAgentStatusEvent? AgentStatus = null);

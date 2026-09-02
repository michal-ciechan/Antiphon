using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record OrchestratorTickResult(
    bool Paused,
    int EligibleCards,
    int Dispatched,
    int Reconciled,
    int SkippedGlobalConcurrency,
    int SkippedColumnConcurrency,
    int ClaimedElsewhere,
    int Failures);

/// <summary>
/// How a Running Sessions row was produced. Card = card-spawn path
/// (<c>AgentSession.CardId</c>); Delegation = current session of a Dispatched/Working
/// non-Check <c>AgentTask</c>. A session that is both is one Card row with the task filled.
/// </summary>
public enum OrchestratorSessionSource
{
    Card = 0,
    Delegation = 1,
}

public sealed record OrchestratorStateDto(
    bool Paused,
    bool Enabled,
    DateTime GeneratedAt,
    int RunningSessions,
    int RunningCardSessions,
    int RunningDelegateSessions,
    int RetryQueueLength,
    OrchestratorStateTotalsDto Totals,
    OrchestratorStateLimitsDto Limits,
    IReadOnlyList<OrchestratorRunningSessionDto> Running,
    IReadOnlyList<OrchestratorRetryQueueItemDto> RetryQueue);

public sealed record OrchestratorStateTotalsDto(
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    long ActiveRuntimeSeconds);

public sealed record OrchestratorStateLimitsDto(
    int PollIntervalSeconds,
    int MaxDispatchesPerTick,
    int FailureBackoffBaseMs,
    int FailureBackoffMaxMs,
    int StartingSessionGraceSeconds);

public sealed record OrchestratorRunningTaskDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskRole Role,
    AgentTaskStatus Status,
    AgentTaskKind Kind,
    Guid RootTaskId,
    Guid? ParentTaskId,
    string? AgentName);

public sealed record OrchestratorRunningSessionDto(
    Guid SessionId,
    OrchestratorSessionSource Source,
    int Depth,
    Guid? CardId,
    string? CardIdentifier,
    string? CardTitle,
    Guid? BoardId,
    string? BoardName,
    OrchestratorRunningTaskDto? Task,
    string DefinitionName,
    string AgentKind,
    string Status,
    Guid? RunAttemptId,
    int TurnCount,
    int? AttemptNumber,
    string? Phase,
    DateTime StartedAt,
    DateTime LastSeenAt,
    DateTime? LastEventAt,
    long RuntimeSeconds,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    bool Live,
    long LastSequence);

public sealed record OrchestratorRetryQueueItemDto(
    Guid CardId,
    string CardIdentifier,
    string CardTitle,
    Guid BoardId,
    string BoardName,
    int AttemptCount,
    int MaxAttempts,
    DateTime? NextRetryAt,
    DateTime? LastAttemptAt,
    string? LastError);

public sealed record OrchestratorPauseResult(bool Paused);

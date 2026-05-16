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

public sealed record OrchestratorStateDto(
    bool Paused,
    bool Enabled,
    DateTime GeneratedAt,
    int RunningSessions,
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

public sealed record OrchestratorRunningSessionDto(
    Guid SessionId,
    Guid CardId,
    string CardIdentifier,
    string CardTitle,
    Guid BoardId,
    string BoardName,
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

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
    int RunningSessions,
    int RetryQueueLength);

public sealed record OrchestratorPauseResult(bool Paused);

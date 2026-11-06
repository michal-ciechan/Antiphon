namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0727 D-6. The directory's synchronous copy of one <c>SessionRunnerStates</c> row.
/// Null timestamps and a false <see cref="Draining"/> are an id that has never been drained.
/// </summary>
public sealed record RunnerState(
    bool Draining,
    DateTimeOffset? DrainedAt,
    string? DrainReason,
    string? RedirectTo,
    bool RetireWhenIdle,
    DateTimeOffset? IdleObservedAt,
    DateTimeOffset? RetiredAt,
    string? RetireReason);

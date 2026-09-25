using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Stops a delegate's live session. A narrow seam over <c>AgentSessionService</c> so the task
/// service can end work in flight without taking on the whole session stack (and so tests can
/// assert that Cancel/Escalate actually stop the delegate rather than just relabelling the row).
///
/// This matters because a Cancel that leaves the delegate running is a lie: the board says the
/// work stopped while a Claude keeps spending against the run's cost ceiling.
/// </summary>
public interface IDelegateSessionStopper
{
    Task KillAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// CARD-0691 D-5: the same stop with the caller's termination source (an agent delete is an
    /// <see cref="SessionTerminationSource.OperatorRequest"/>). <c>AgentSessionService</c> implements
    /// it directly; a seam without the notion falls back to the one-argument stop.
    /// </summary>
    Task KillAsync(Guid sessionId, SessionTerminationSource source, CancellationToken ct) =>
        KillAsync(sessionId, ct);
}

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
}

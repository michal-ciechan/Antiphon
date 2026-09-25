namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0716 D-2: the launch queue's idle wait, so a shutdown can drain in-flight starts
/// without taking a dependency on the queue type.
/// </summary>
public interface ILaunchDrain
{
    /// <summary>
    /// Returns when no launch is in flight. Throws <see cref="OperationCanceledException"/> when
    /// <paramref name="timeout"/> elapses or <paramref name="ct"/> is cancelled.
    /// </summary>
    Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct);
}

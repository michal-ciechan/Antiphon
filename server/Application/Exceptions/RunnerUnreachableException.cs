namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0511 D-2. Nobody answered <c>GET /capabilities</c>, so a launch that needs positive
/// capability evidence cannot be decided at all. Deliberately NOT a
/// <see cref="RunnerCapabilityMismatchException"/>: "the runner is stale, rebuild it" and "the
/// runner did not answer, wait" are different operator actions and different restart
/// classifications — this one is ordinary transport, paced by the ordinary ladder.
/// </summary>
public sealed class RunnerUnreachableException(string message, Exception? innerException)
    : Exception(message, innerException);

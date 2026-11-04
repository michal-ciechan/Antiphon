namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-1705: a bounded enqueue did not get the session's queue lock in time. Nothing was
/// written; the caller retries later instead of waiting behind that session's delivery.
/// </summary>
public sealed class SessionQueueBusyException(Guid sessionId)
    : Exception($"Session {sessionId} queue lock is held; the enqueue was not attempted.")
{
    public Guid SessionId { get; } = sessionId;
}

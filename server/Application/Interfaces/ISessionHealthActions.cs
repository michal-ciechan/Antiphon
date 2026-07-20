namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// The side-effect seam for session health repair, so the state machine in SessionHealthService
/// is testable without a live runner/queue. Production impl: Infrastructure.Supervision.SessionHealthActions.
/// </summary>
public interface ISessionHealthActions
{
    /// <summary>Queue text into the session's composer, delivered when the agent is idle.</summary>
    Task EnqueueWhenIdleAsync(Guid sessionId, string text, CancellationToken ct);

    /// <summary>Kill the session process (the supervisor's ladder then restarts always-on agents).</summary>
    Task KillSessionAsync(Guid sessionId, CancellationToken ct);

    /// <summary>Rendered terminal screen text (empty when unavailable).</summary>
    Task<string> SnapshotScreenAsync(Guid sessionId, CancellationToken ct);

    /// <summary>Raw PTY input (no Enter unless included).</summary>
    Task SendRawInputAsync(Guid sessionId, string input, CancellationToken ct);
}

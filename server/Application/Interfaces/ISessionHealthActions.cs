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

    /// <summary>
    /// CARD-0292 S5: if the rendered screen shows the /remote-control management menu
    /// (<c>RemoteControlMenuScreen.IsPresent</c>), send one Esc — via the queue runtime's
    /// idle-guarded raw-input path, never while the session is working — and report whether a
    /// menu was seen and dismissed. False = no menu (the normal case; costs one snapshot) or
    /// the guard withheld the keystroke.
    /// </summary>
    Task<bool> TryDismissRemoteControlMenuAsync(Guid sessionId, CancellationToken ct);
}

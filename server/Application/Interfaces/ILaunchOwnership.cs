namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// In-process registry of session launches this server is currently running (CARD-0340).
/// A <c>Starting</c> row the runner still serves that no launch in this process owns is an
/// interrupted launch: the only thing that could flip it lived in the process that died.
/// </summary>
public interface ILaunchOwnership
{
    bool Owns(Guid sessionId);

    /// <summary>
    /// Claim the session and resume its launch on a background task. A second call for an id
    /// this process already owns is a no-op.
    /// </summary>
    void ResumeInterrupted(Guid sessionId, Guid agentId);

    /// <summary>Returns false when this process already owns the id.</summary>
    bool TryRegister(Guid sessionId);

    void Unregister(Guid sessionId);
}

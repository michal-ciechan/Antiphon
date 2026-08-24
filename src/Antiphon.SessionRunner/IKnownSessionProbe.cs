namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0181 C0: whether this runner launched a session (a sidecar exists for it). Null probe
/// disables C0, the same convention <c>claims: null</c> uses.
/// </summary>
internal interface IKnownSessionProbe
{
    bool Exists(Guid sessionId);
}

/// <summary>A sidecar on disk is existence of the session as far as this runner is concerned.</summary>
internal sealed class SidecarKnownSessionProbe(string sessionLogPath) : IKnownSessionProbe
{
    public bool Exists(Guid sessionId) =>
        File.Exists(TranscriptSidecar.PathFor(sessionLogPath, sessionId));
}

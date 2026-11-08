using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1766: pty sequence polls read the session record. Herdr and an omitted
/// backend read the buffer route, which carries the runner's current LastSequence
/// without pane.get, process-info or a status refresh. A poll never answers from
/// a stored copy.
/// </summary>
public static class SequencePollRoute
{
    public static bool UsesSessionRecord(string? backend) =>
        string.Equals(backend, SessionBackends.PtyHost, StringComparison.Ordinal);
}

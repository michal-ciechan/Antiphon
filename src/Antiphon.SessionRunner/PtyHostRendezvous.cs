using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1653 S2: the one choice of a fresh launch's pipe name.</summary>
internal static class PtyHostRendezvous
{
    public static string PipeNameFor(SessionRunnerSettings settings, RunnerLaunchRequest request, bool isWindows) =>
        PtyHostProtocol.PipeNameFor(request.SessionId);
}

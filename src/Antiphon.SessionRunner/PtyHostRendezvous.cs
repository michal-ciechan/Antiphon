using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1653 S2: the one choice of a fresh launch's pipe name.</summary>
internal static class PtyHostRendezvous
{
    /// <summary>
    /// Only an Owned, non-Windows, non-custody launch uses the owned directory. Windows always keeps
    /// <c>antiphon-pty-&lt;id&gt;</c>, and custody keeps the well-known name its ledger pins
    /// (RunnerCustodyLedger.RecoverManifests). The legacy name is the platform's
    /// <c>PtyHostProtocol.PipeNameFor</c> spelling.
    /// </summary>
    public static string PipeNameFor(SessionRunnerSettings settings, RunnerLaunchRequest request, bool isWindows)
    {
        var name = $"antiphon-pty-{request.SessionId:N}";
        if (isWindows) return name;
        if (settings.PtyHostSocketDirMode == PtyHostSocketDirMode.Owned && request.VerificationBinding is null)
            return settings.ResolvedPtyHostSocketDir + "/" + request.SessionId.ToString("N");
        return "/tmp/" + name;
    }
}

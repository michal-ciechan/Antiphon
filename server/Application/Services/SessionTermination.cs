using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// First-writer-wins stamp for <see cref="AgentSession.TerminationSource"/> (CARD-0256, CARD-0316).
/// Platform closers record a source with the terminal status write; an exit event never erases a
/// request, and a request never writes <see cref="SessionTerminationSource.Unknown"/>.
/// ProcessExit records an observation, never voluntary exit or a signal sender.
/// </summary>
public static class SessionTermination
{
    /// <summary>First writer wins (CARD-0256). Returns true when it wrote.</summary>
    public static bool Record(AgentSession session, SessionTerminationSource source)
    {
        if (source == SessionTerminationSource.Unknown
            || session.TerminationSource != SessionTerminationSource.Unknown)
            return false;
        session.TerminationSource = source;
        return true;
    }

    /// <summary>
    /// Preserve legacy/no-signal source behavior. A known signal plus a cause token alone
    /// cannot manufacture an independently stored platform request; require live issued evidence.
    /// </summary>
    public static SessionTerminationSource FromExitReason(AgentExitReason reason, int? signal = null,
        ProcessStopIntentDto? intent = null) =>
        reason is AgentExitReason.CpuSpinKilled or AgentExitReason.MemoryKilled
            && (signal is null or 0 || intent is { Provenance: ProcessStopProvenance.LiveIssuer,
                Outcome: ProcessStopOutcome.Issued, Target: ProcessStopTarget.Child or ProcessStopTarget.Custody }
                && intent.AttemptedSignal == signal)
            ? SessionTerminationSource.SystemRequest
            : SessionTerminationSource.ProcessExit;
}

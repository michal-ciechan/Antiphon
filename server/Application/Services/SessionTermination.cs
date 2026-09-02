using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// First-writer-wins stamp for <see cref="AgentSession.TerminationSource"/> (CARD-0256, CARD-0316).
/// Platform closers record a source with the terminal status write; an exit event never erases a
/// request, and a request never writes <see cref="SessionTerminationSource.Unknown"/>.
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

    /// <summary>A runner-side watchdog kill is the platform deciding, not the process leaving.</summary>
    public static SessionTerminationSource FromExitReason(AgentExitReason reason) =>
        reason is AgentExitReason.CpuSpinKilled or AgentExitReason.MemoryKilled
            ? SessionTerminationSource.SystemRequest
            : SessionTerminationSource.ProcessExit;
}

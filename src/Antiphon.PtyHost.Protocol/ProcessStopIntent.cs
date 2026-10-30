using Antiphon.SessionRunner.Contracts;

namespace Antiphon.PtyHost.Protocol;

/// <summary>Owner-local attempt shape; S4 supplies issuer memory and the diagnostic writer.</summary>
public sealed record ProcessStopIntent : ProcessStopIntentDto
{
    /// <summary>Files are agent-writable diagnostics, never live issuer authority.</summary>
    public static ProcessStopIntentDto? Recovered(ProcessStopIntentDto? value) => value is null
        ? null : value with { Provenance = ProcessStopProvenance.RecoveredUnverified };
}

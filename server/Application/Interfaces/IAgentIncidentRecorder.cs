using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// The supervisor's incident write: one <c>AgentIncidents</c> row plus the matching alert.
/// CARD-0245 S1 uses this so a watchdog-state reader can reuse the ordinary incident path
/// without constructing the rest of <c>AgentSupervisorService</c> in tests.
/// </summary>
public interface IAgentIncidentRecorder
{
    Task RecordIncidentAsync(
        Guid? agentId,
        Guid? sessionId,
        AgentIncidentKind kind,
        AlertSeverity severity,
        string message,
        int? exitCode = null,
        string? failureReason = null,
        bool raiseAlert = true,
        CancellationToken ct = default);
}

using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Append-only record of something happening to a supervised agent — crashes, scheduled restarts,
/// recoveries, escalations, suspensions. The audit trail behind the agent card's incident drawer
/// and (later) the alert pipeline. Pruned by retention (default 30 days / 500 per agent).
/// </summary>
public class AgentIncident
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid? SessionId { get; set; }
    public AgentIncidentKind Kind { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }

    public Agent? Agent { get; set; }
}

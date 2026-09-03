using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Append-only record of something happening to a supervised agent — crashes, scheduled restarts,
/// recoveries, escalations, suspensions. The audit trail behind the agent card's incident drawer
/// and (later) the alert pipeline. Pruned by retention (default 30 days / 500 per agent).
/// </summary>
public class AgentIncident
{
    // See Alert: the ceilings live beside the properties so the model and the clip agree.
    public const int MessageMaxLength = 4000;
    public const int FailureReasonMaxLength = 4000;

    public Guid Id { get; set; }

    /// <summary>
    /// Null when the session has no standing agent (the operator's own orchestrator session is
    /// the CARD-0247 founding case). The row still attaches via <see cref="SessionId"/>.
    /// </summary>
    public Guid? AgentId { get; set; }
    public Guid? SessionId { get; set; }
    public AgentIncidentKind Kind { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// CARD-0338 S3: when the digest pager sent this incident to DigestEnabled channels.
    /// Not an acknowledgement — a second loss an hour later is a second page. Backfilled to
    /// <see cref="CreatedAt"/> on migrate so a deploy never pages history.
    /// </summary>
    public DateTime? HumanNotifiedAt { get; set; }

    public Agent? Agent { get; set; }
}

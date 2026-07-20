using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A structured operational alert (spec: 2026-07-20-always-on-agents-and-alerting.md part B).
/// The DB row is the source of truth/audit; SignalR and channel sinks are projections.
/// </summary>
public class Alert
{
    public Guid Id { get; set; }
    public AlertSeverity Severity { get; set; }

    /// <summary>Producer: supervisor | reconciler | launch | bridge | runner | log | watchdog.</summary>
    public string Source { get; set; } = string.Empty;

    public Guid? AgentId { get; set; }
    public Guid? SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }

    /// <summary>Grouping key for the routing throttle's dedup/digest.</summary>
    public string DedupKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? RoutedAt { get; set; }

    /// <summary>Repeats collapsed into this alert by the routing throttle.</summary>
    public int SuppressedCount { get; set; }
}

using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// A structured operational alert (spec: 2026-07-20-always-on-agents-and-alerting.md part B).
/// The DB row is the source of truth/audit; SignalR and channel sinks are projections.
/// </summary>
public class Alert
{
    // The column ceilings, stated next to the properties they govern. AppDbContext's model and the
    // write path's clip both read them, so schema and clip cannot drift apart. A varchar overflow
    // here is not an exception somewhere harmless: it is the report of a problem dying of the size
    // of the problem it was reporting (CARD-0195, and again CARD-0205).
    public const int SourceMaxLength = 50;
    public const int TitleMaxLength = 500;
    public const int DetailMaxLength = 4000;
    public const int DedupKeyMaxLength = 500;

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

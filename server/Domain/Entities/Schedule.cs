using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One clock-driven action (CARD-0057). A schedule is N:1 with its target, outlives any one
/// session, and has its own lifecycle (enable, edit, history). Prompt and card kinds share this
/// row; phase 1 fires prompts only.
/// </summary>
public class Schedule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ScheduleKind Kind { get; set; }
    public ScheduleRepeat Repeat { get; set; }

    /// <summary>IANA or Windows id. Validated at create with <c>TimeZoneInfo.FindSystemTimeZoneById</c>.</summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>Next due instant, UTC. Null means disarmed (Once after it fires, or budget spent).</summary>
    public DateTime? NextFireAt { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the one overdue claim after downtime still fires. Null = unlimited (Once default).
    /// Daily default 60; Interval default min(every, 60).
    /// </summary>
    public int? MissedGraceMinutes { get; set; }

    public int FireCount { get; set; }
    public DateTime? LastFiredAt { get; set; }
    public ScheduleFireOutcome? LastOutcome { get; set; }
    public string? LastOutcomeDetail { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    // ---- Prompt ----
    public Guid? AgentId { get; set; }
    public string? PromptText { get; set; }
    public ScheduleWhenTargetDown WhenTargetDown { get; set; }

    // ---- Card (phase 2 columns; unused by the prompt fire arm) ----
    public Guid? CardId { get; set; }
    public CardStatus? TargetStatus { get; set; }
    public ScheduleStart Start { get; set; }
    public DateTime? SpendAcceptedAt { get; set; }
    public string? SpendAcceptedBy { get; set; }

    // ---- Recurrence payload ----
    /// <summary>Once: the UTC instant.</summary>
    public DateTime? FireAt { get; set; }

    /// <summary>Interval: 1..10080 minutes.</summary>
    public int? EveryMinutes { get; set; }

    /// <summary>Interval: UTC anchor so ticks never drift.</summary>
    public DateTime? AnchorAt { get; set; }

    /// <summary>Daily: local wall time "HH:mm".</summary>
    public string? AtLocal { get; set; }

    /// <summary>
    /// Daily: Mon=bit 0 .. Sun=bit 6. Zero means all days (the create default).
    /// </summary>
    public int DaysOfWeek { get; set; }

    public Agent? Agent { get; set; }
    public Card? Card { get; set; }
    public ICollection<ScheduleFire> Fires { get; set; } = new List<ScheduleFire>();
}

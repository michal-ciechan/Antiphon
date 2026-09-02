using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One claimed fire of a <see cref="Schedule"/> (CARD-0057). Written inside the claim so a crash
/// between claim and run leaves a row stuck at <see cref="ScheduleFireOutcome.Claimed"/> rather
/// than a silent gap. Unique on (ScheduleId, FireNumber).
/// </summary>
public class ScheduleFire
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public int FireNumber { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ScheduleFireOutcome Outcome { get; set; } = ScheduleFireOutcome.Claimed;
    public string? Detail { get; set; }
    public Guid? QueuedMessageId { get; set; }
    public Guid? SpawnedSessionId { get; set; }

    /// <summary>True for POST .../fire-now: bypasses grace and does not advance recurrence.</summary>
    public bool Manual { get; set; }

    public Schedule Schedule { get; set; } = null!;
}

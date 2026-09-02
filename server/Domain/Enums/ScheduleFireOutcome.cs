namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// What happened to one claimed fire (CARD-0057). Numbering is the contract: append, never
/// renumber. Eleven named outcomes.
/// </summary>
public enum ScheduleFireOutcome
{
    /// <summary>Written inside the claim, before the worker runs.</summary>
    Claimed = 0,

    /// <summary>WhenIdle delivered immediately into an idle live session.</summary>
    Delivered = 1,

    /// <summary>Queued WhenIdle; the session is busy or still starting.</summary>
    Enqueued = 2,

    /// <summary>No live session; always-on (or Queue policy). The relaunch carry-over delivers it.</summary>
    QueuedForRelaunch = 3,

    /// <summary>No live session and nobody will start one; or never launched.</summary>
    SkippedNoSession = 4,

    /// <summary>The one overdue claim was past MissedGraceMinutes. Recurring only.</summary>
    SkippedLate = 5,

    /// <summary>Card target gone (archived, owned, terminal, NeedsDecision). Phase 2.</summary>
    SkippedTargetGone = 6,

    /// <summary>Card Start=None moved. Phase 2.</summary>
    Moved = 7,

    /// <summary>Card Start=Release moved and lifted the hold. Phase 2.</summary>
    Released = 8,

    /// <summary>Card Start=Spawn launched. Phase 2.</summary>
    Spawned = 9,

    /// <summary>Fire-time refusal (quota 409, forbidden body). Never retried, never rerouted.</summary>
    Refused = 10,

    /// <summary>The worker threw. The schedule has already moved on; not reclaimed.</summary>
    Failed = 11,
}

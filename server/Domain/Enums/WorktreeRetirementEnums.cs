namespace Antiphon.Server.Domain.Enums;

public enum WorktreeRetirementState
{
    Released = 0,
    Claimed = 1,
    CommandStarted = 2,
    Partial = 3,
    Complete = 4,
    Refused = 5,
    Revoked = 6
}

public enum WorktreeHandoffDispositionKind
{
    Consumed = 0,
    Superseded = 1,
    Canceled = 2,
    MissingReviewed = 3
}

public enum WorktreeResidueLane
{
    SettledTask = 0,
    Publication = 1,
    Inventory = 2
}

public enum WorktreeResidueCandidateOutcome
{
    Preview = 0,
    Held = 1,
    Deferred = 2,
    Queued = 3,
    Refused = 4,
    Partial = 5,
    Removed = 6
}

public enum WorkspaceReservationKind
{
    Launch = 0,
    Retirement = 1,
    HistoricalFence = 2
}

public enum LandRequestOrigin
{
    ExplicitCaller = 0,
    ScheduledCleanup = 1
}

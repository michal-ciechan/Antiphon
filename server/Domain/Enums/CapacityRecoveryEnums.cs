namespace Antiphon.Server.Domain.Enums;

/// <summary>Why a <see cref="Entities.ModelAvailabilityHold"/> was cleared (CARD-0412). Null on uncleared rows; unknown on legacy already-cleared rows.</summary>
public enum ModelAvailabilityClearCause
{
    Expired = 0,
    OperatorCleared = 1,
}

/// <summary>Which logical consumer a capacity-recovery episode belongs to.</summary>
public enum CapacityWaitConsumerKind
{
    LiveSession = 0,
    PendingQueue = 1,
    QueuedTask = 2,
    RoutingBlockedTask = 3,
    StandingStart = 4,
    CreateRefusal = 5,
}

/// <summary>
/// Live path: WaitingForHold → Ready → ActionPending → Admitted → PromptConfirmed → Progressed.
/// Launch can record StartAccepted then Running. Side outcomes: Deferred, Superseded, Canceled, Reheld, Exhausted.
/// </summary>
public enum CapacityRecoveryWaitState
{
    WaitingForHold = 0,
    Ready = 1,
    ActionPending = 2,
    Admitted = 3,
    StartAccepted = 4,
    Running = 5,
    PromptConfirmed = 6,
    Progressed = 7,
    Deferred = 8,
    Superseded = 9,
    Canceled = 10,
    Reheld = 11,
    Exhausted = 12,
}

/// <summary>Where <see cref="Entities.ApiErrorRecovery.EvidenceAt"/> came from.</summary>
public enum CapacityEvidenceTimestampSource
{
    TurnEndTimestamp = 0,
    AssistantTextTimestamp = 1,
    TurnEndCreatedAt = 2,
    DetectedAt = 3,
}

/// <summary>Compatibility observation distinct from a stored clear cause.</summary>
public enum CapacityRecoveryCompatibilityResult
{
    Linked = 0,
    LegacyAvailable = 1,
    SkippedAmbiguous = 2,
    SkippedTerminal = 3,
    SkippedProgress = 4,
}

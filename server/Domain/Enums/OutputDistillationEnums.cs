namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0330: whether a successful distillation replaces the queued completion note.
/// Shadow is 0 so an unbound setting still ships as record-only.
/// </summary>
public enum OutputDistillerMode
{
    Shadow = 0,
    Apply = 1,
}

/// <summary>
/// What happened to one distillation request (CARD-0330 D7). Applied is the only outcome that
/// replaced a queued completion note; every other value leaves today's raw note in place.
/// </summary>
public enum DistillationOutcome
{
    Applied = 0,
    AppliedLate = 1,
    Shadowed = 2,
    RejectedOverCompressed = 3,
    RejectedUnderCompressed = 4,
    DegradedUnavailable = 5,
    DegradedBusy = 6,
    DegradedTimeout = 7,
    DegradedEmpty = 8,
    DegradedFailed = 9,
    SkippedShort = 10,
    SkippedLong = 11,
}

/// <summary>Explicit flag on a distillation (CARD-0330 D7). None is the column default.</summary>
public enum DistillationFeedback
{
    None = 0,
    Good = 1,
    LostInformation = 2,
    Noisy = 3,
}

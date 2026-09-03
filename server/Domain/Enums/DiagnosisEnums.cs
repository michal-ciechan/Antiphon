namespace Antiphon.Server.Domain.Enums;

/// <summary>Which diagnose job produced a <c>Diagnoses</c> row (CARD-0352).</summary>
public enum DiagnosisKind
{
    Title = 0,
    Labels = 1,
}

/// <summary>
/// What happened to one diagnose request. Applied is the only outcome that changed the
/// titled task or labelled card; every other value leaves today's behaviour in place.
/// </summary>
public enum DiagnosisOutcome
{
    Applied = 0,
    Shadowed = 1,
    Unclear = 2,
    SkippedAlreadyTitled = 3,
    SkippedAlreadyLabelled = 4,
    RejectedGate = 5,
    RejectedUnparseable = 6,
    RejectedConflict = 7,
    DegradedHeld = 8,
    DegradedBudget = 9,
    DegradedBusy = 10,
    DegradedTimeout = 11,
    DegradedFailed = 12,
    DegradedEmpty = 13,
    DegradedUnavailable = 14,
}

namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Outcome of one queue-delivery attempt. Null on the row means the process died after the
/// pre-type <c>Sent</c> stamp and before a verdict was recorded (CARD-0340 S3). A known
/// <see cref="NoSubmitOutput"/> survives the revert to Pending so the stranded sweep can
/// Enter-only retry a body that is still on screen (CARD-0342).
/// </summary>
public enum DeliveryVerdict
{
    Delivered = 0,
    NoComposerEvidence = 1,
    NoSubmitOutput = 2,
    NoTranscriptRecord = 3,
    Truncated = 4,
    ForbiddenBody = 5,
    LocalCommandNotAccepted = 6,
    BackendUnreachable = 7,
    LateConfirmed = 8,
}

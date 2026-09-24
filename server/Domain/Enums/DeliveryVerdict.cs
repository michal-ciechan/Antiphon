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

    /// <summary>
    /// CARD-0514: an open remote-control modal withheld this delivery. Not a failure: no attempt
    /// charge, park, or kill.
    /// </summary>
    ModalBlocked = 9,

    /// <summary>
    /// CARD-0647: the typed text is a runner spill pointer and the row has no file bytes to send.
    /// The message is canceled; retrying it would point the agent at a file nobody can write.
    /// </summary>
    SpillBodyMissing = 10,
}

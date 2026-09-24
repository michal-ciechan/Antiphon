namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// One open condition per directive, kind and subject. Detection is later; S1 only stores the row.
/// </summary>
public enum ExpectationEpisodeKind
{
    /// <summary>Queued-stint pipeline debt. HeldAged does not reset the clock. S2 owns detection.</summary>
    StalledPipeline = 0,

    /// <summary>Dispatch-wide fence for a scope. S5 owns the status read; pipeline and health do not.</summary>
    DispatchFence = 1,

    /// <summary>Runner lane below its in-flight target. S2 owns detection.</summary>
    CapacityDeficit = 2,

    /// <summary>Dispatched work with no usable session or a rolled-up progress stall. S3 owns detection.</summary>
    SilentInFlight = 3,

    /// <summary>Caller note still unconfirmed. S3 owns detection. CARD-0641 keeps ordinary note recovery.</summary>
    UndeliveredNote = 4,
}

/// <summary>Direct-send progress. S1 records None; S4 owns Attempting and receipt.</summary>
public enum ExpectationAttemptState
{
    None = 0,
    Attempting = 1,
    Confirmed = 2,
    Uncertain = 3,
    /// <summary>No receipt and no evidence the body left the composer: Enter withheld, NoSubmitOutput,
    /// NoTranscriptRecord, or a screen-only verdict. Ordinary input holds until a transcript record
    /// shows the prompt, the body is no longer visible whole, or the generation changes.</summary>
    Unconfirmed = 4,
    Refused = 5,

    /// <summary>
    /// CARD-0650 S4 repair D1. A submitted-prompt record carries the body, but it cannot confirm:
    /// the whole body with no transcript floor, or a Truncated record. Operator debt like
    /// Unconfirmed, and a late receipt can still confirm it, but it never holds ordinary input:
    /// the body left the composer and its echo stays on screen.
    /// </summary>
    Submitted = 6,
}

/// <summary>
/// Operator page outbox. Due is 1 because the pending-delivery index filter compares that value.
/// S5 owns publication. S1 leaves the outbox None.
/// </summary>
public enum ExpectationOperatorOutboxState
{
    None = 0,
    Due = 1,
    Published = 2,
    Suppressed = 3,
    Unsent = 4,
}

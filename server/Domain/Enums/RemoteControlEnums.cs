namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0514: durable discriminator for an automatic remote-control arm request.
/// Ordinary queue rows stay <see cref="None"/>. Do not invent provenance from body text.
/// </summary>
public enum RemoteControlMaintenanceKind
{
    None = 0,
    AutomaticArm = 1,
    LegacyUnclassified = 2,
}

/// <summary>Typed outcome of an automatic <c>/remote-control</c> arm attempt. Distinct from <see cref="DeliveryVerdict"/>.</summary>
public enum RemoteControlArmResult
{
    Requested = 0,
    SuppressedAlreadyArmed = 1,
    SupersededGeneration = 2,
    WithheldUnknown = 3,
    SubmissionStarted = 4,
    ArmedObserved = 5,
    ArmUnconfirmed = 6,
    WithheldMenuPresent = 7,
    WithheldNotIdle = 8,
    WithheldCapability = 9,
    WithheldStopping = 10,
    WithheldLaunchOwner = 11,
    WithheldTransport = 12,
}

/// <summary>How a <see cref="Entities.RemoteControlModalEpisode"/> closed.</summary>
public enum RemoteControlEpisodeResolution
{
    DismissedVerified = 0,
    ObservedClear = 1,
    GenerationEnded = 2,
    DismissUnverified = 3,
}

/// <summary>Latest dismissal/withhold reason recorded on an episode. Not a delivery verdict.</summary>
public enum RemoteControlDismissalResult
{
    WithheldWorking = 0,
    WithheldUnboundTranscript = 1,
    WithheldStaleSnapshot = 2,
    WithheldUnsupportedTransport = 3,
    WithheldPartialScreen = 4,
    EscSentUnverified = 5,
    DismissedVerified = 6,
    ObservedClear = 7,
    GenerationMismatch = 8,
    WithheldUnprovenGeneration = 9,
    DetectionOnly = 10,
}

/// <summary>Current-child bridge classification. Unknown never authorizes automatic RC input.</summary>
public enum RemoteControlBridgeState
{
    Unarmed = 0,
    Armed = 1,
    Unknown = 2,
}

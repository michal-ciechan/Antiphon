namespace Antiphon.Server.Domain.Enums;

public enum RestartFailureKind
{
    Unknown,
    Infrastructure,
    LaunchOrProcessFailure,
    ContinuityUnavailable,
}

public enum StandingContinuityReason
{
    NativeSessionMissing,
    TargetMissing,
    TargetIncompatible,
    OwnershipUnproven,
}

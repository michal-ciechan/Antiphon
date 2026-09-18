namespace Antiphon.Server.Domain.Enums;

public enum RestartFailureKind
{
    Unknown,
    Infrastructure,
    LaunchOrProcessFailure,
    ContinuityUnavailable,

    /// <summary>
    /// CARD-0511. The connected session runner is an older build than this server needs. Never
    /// charged to the backoff ladder: the standing agent holds until a different runner identity
    /// answers, or an operator asserts the fix with a manual Start.
    /// </summary>
    RunnerBuildStale,
}

public enum StandingContinuityReason
{
    NativeSessionMissing,
    TargetMissing,
    TargetIncompatible,
    OwnershipUnproven,
}

namespace Antiphon.Server.Domain.Enums;

/// <summary>Typed attempt verdict; never infer contract/capacity classification from free text.</summary>
public enum SpecialistAttemptOutcome
{
    ValidReading = 0,
    InvalidReading = 1,
    Empty = 2,
    ToolAttempt = 3,
    IdentityMismatch = 4,
    InputUnsupported = 5,
    TimedOutAfterDispatch = 6,
    DeliveryUnconfirmed = 7,
    ExpiredBeforeDispatch = 8,
    TaskFailedTransport = 9,
    TaskFailedUnknown = 10,
    Held = 11,
    QuotaUnavailable = 12,
    AuthenticationUnavailable = 13,
    Busy = 14,
    DeclaredButUnprovisioned = 15,
    Disabled = 16,
    CallerCanceled = 17,
    HostShutdown = 18,
    ValidQualificationBatch = 19,
}

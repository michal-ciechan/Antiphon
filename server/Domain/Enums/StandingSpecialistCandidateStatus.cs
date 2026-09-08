namespace Antiphon.Server.Domain.Enums;

public enum StandingSpecialistCandidateStatus
{
    DeclaredButUnprovisioned = 0,
    Unqualified = 1,
    Qualifying = 2,
    Qualified = 3,
    Quarantined = 4,
    Unsupported = 5,
    PendingDependency = 6,
    Disabled = 7,
}

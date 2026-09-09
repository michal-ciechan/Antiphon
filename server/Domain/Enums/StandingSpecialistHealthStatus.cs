namespace Antiphon.Server.Domain.Enums;

public enum StandingSpecialistHealthStatus
{
    Healthy = 0,
    DegradedReadiness = 1,
    UsingFallback = 2,
    Suspect = 3,
    Unavailable = 4,
    Disabled = 5,
}

public enum SpecialistRequestPurpose { Check = 0, Qualification = 1 }
public enum SpecialistRequestStatus { Pending = 0, Running = 1, Succeeded = 2, Failed = 3, Expired = 4, Canceled = 5 }

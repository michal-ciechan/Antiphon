namespace Antiphon.Server.Domain.Enums;

public enum TaskSupervisionNoticePurpose
{
    CeilingParent = 0,
    FailureDelegate = 1,
    LateFailedParent = 2,
}

public enum TaskSupervisionNoticeState
{
    Pending = 0,
    Told = 1,
    Superseded = 2,
    NotApplicable = 3,
}

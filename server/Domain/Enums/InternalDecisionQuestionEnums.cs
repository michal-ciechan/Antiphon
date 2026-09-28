namespace Antiphon.Server.Domain.Enums;

public enum InternalDecisionImpact
{
    None = 0,
    ProductBehavior = 1,
    Data = 2,
    Ux = 3,
    PublicContract = 4,
    Security = 5,
    OperationalPolicy = 6,
    ExternalActionOrSpend = 7,
    Mixed = 8,
    Unknown = 9,
}

public enum InternalDecisionDisposition
{
    Continue = 0,
    NeedsHuman = 1,
}

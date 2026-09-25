namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0710. Where a task is allowed to run. <see cref="Any"/> is a real choice, distinct from
/// an omitted request, which inherits the card or follow-up value.
/// </summary>
public enum RequiredPlatform
{
    Any = 0,
    Windows = 1,
    Linux = 2,
}

namespace Antiphon.Server.Domain.Enums;

/// <summary>CARD-0710. Why a task's <see cref="RequiredPlatform"/> was frozen at create.</summary>
public enum RequirementSource
{
    Default = 0,
    Card = 1,
    Request = 2,
    FollowUp = 3,
}

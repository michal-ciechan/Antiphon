namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Abstraction for the currently authenticated user.
/// MVP: resolved to hardcoded default admin + client IP.
/// Future: OIDC middleware swaps implementation with zero refactoring.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string UserName { get; }
    string IpAddress { get; }
}

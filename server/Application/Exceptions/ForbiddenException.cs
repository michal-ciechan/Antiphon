namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Thrown when the current user lacks permission. Maps to HTTP 403.
/// </summary>
public class ForbiddenException : HttpException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(403, message)
    {
    }
}

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Thrown when an operation conflicts with the current state. Maps to HTTP 409.
/// </summary>
public class ConflictException : HttpException
{
    public ConflictException(string message) : base(409, message)
    {
    }
}

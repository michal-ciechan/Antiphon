namespace Antiphon.Server.Application.Exceptions;

/// <summary>Maps to HTTP 400. Used for invalid request inputs.</summary>
public sealed class BadRequestException : HttpException
{
    public BadRequestException(string message, string code = "bad_request") : base(400, message, code)
    {
    }
}

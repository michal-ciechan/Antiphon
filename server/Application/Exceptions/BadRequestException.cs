namespace Antiphon.Server.Application.Exceptions;

/// <summary>Maps to HTTP 400. Used for request bodies that are well-formed JSON but unusable (CARD-0179).</summary>
public sealed class BadRequestException : HttpException
{
    public BadRequestException(string message) : base(400, message, "bad_request")
    {
    }
}

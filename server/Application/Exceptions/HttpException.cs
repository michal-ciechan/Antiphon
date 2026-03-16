namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Base exception for all HTTP-mapped exceptions. Middleware maps StatusCode to the response.
/// </summary>
public abstract class HttpException : Exception
{
    public int StatusCode { get; }

    protected HttpException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    protected HttpException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

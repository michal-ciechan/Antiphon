namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Base exception for all HTTP-mapped exceptions. Middleware maps StatusCode to the response.
/// </summary>
public abstract class HttpException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }

    protected HttpException(int statusCode, string message, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    protected HttpException(
        int statusCode,
        string message,
        Exception innerException,
        string? code = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Base exception for all HTTP-mapped exceptions. Middleware maps StatusCode to the response.
/// </summary>
public abstract class HttpException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }

    /// <summary>
    /// Optional extra members merged into the RFC 9457 problem document next to <c>code</c>.
    /// Null for every existing exception; CARD-0136 uses it for the <c>quota</c> extension.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; }

    protected HttpException(
        int statusCode,
        string message,
        string? code = null,
        IReadOnlyDictionary<string, object?>? extensions = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Extensions = extensions;
    }

    protected HttpException(
        int statusCode,
        string message,
        Exception innerException,
        string? code = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Extensions = extensions;
    }
}

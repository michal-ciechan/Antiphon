namespace Antiphon.Server.Application.Exceptions;

public class ServiceUnavailableException : HttpException
{
    public int? RetryAfterSeconds { get; }

    public ServiceUnavailableException(string message, string code)
        : base(503, message, code)
    {
    }

    public ServiceUnavailableException(string message, string code, Exception innerException, int? retryAfterSeconds = null)
        : base(503, message, innerException, code)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}

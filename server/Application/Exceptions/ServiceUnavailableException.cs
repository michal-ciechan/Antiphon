namespace Antiphon.Server.Application.Exceptions;

public sealed class ServiceUnavailableException : HttpException
{
    public ServiceUnavailableException(string message, string code)
        : base(503, message, code)
    {
    }

    public ServiceUnavailableException(string message, string code, Exception innerException)
        : base(503, message, innerException, code)
    {
    }
}

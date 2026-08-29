namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0213: a session-runner RFC 9457 problem (inspect/attach/launch) mapped onto the
/// server's HTTP exception seam. Status and <see cref="HttpException.Code"/> are the runner's.
/// </summary>
public sealed class RunnerProblemException : HttpException
{
    public RunnerProblemException(int statusCode, string message, string code)
        : base(statusCode, message, code)
    {
    }

    public RunnerProblemException(int statusCode, string message, string code, Exception innerException)
        : base(statusCode, message, innerException, code)
    {
    }
}

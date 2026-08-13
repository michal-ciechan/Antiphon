namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Thrown when an operation conflicts with the current state. Maps to HTTP 409.
/// </summary>
public class ConflictException : HttpException
{
    public ConflictException(string message) : base(409, message, "conflict")
    {
    }

    public ConflictException(string message, string code) : base(409, message, code)
    {
    }

    /// <summary>
    /// Keeps the underlying failure attached. A 409 raised from a database error is a summary of
    /// something more specific — a unique-index violation names its constraint, a deadlock names
    /// its victim — and discarding it leaves nothing to debug from when the 409 turns out to be a
    /// misdiagnosis. Both the log and the problem-details stack trace walk inner exceptions.
    /// </summary>
    public ConflictException(string message, Exception innerException)
        : base(409, message, innerException, "conflict")
    {
    }

    public ConflictException(string message, Exception innerException, string code)
        : base(409, message, innerException, code)
    {
    }
}

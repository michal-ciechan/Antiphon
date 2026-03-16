namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Thrown when request validation fails. Maps to HTTP 422 with structured errors.
/// </summary>
public class ValidationException : HttpException
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException(Dictionary<string, string[]> errors)
        : base(422, "One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : base(422, "One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>
        {
            { field, [error] }
        };
    }
}

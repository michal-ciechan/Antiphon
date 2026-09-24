namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Thrown when request validation fails. Maps to HTTP 422 with structured errors.
/// </summary>
public class ValidationException : HttpException
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException(Dictionary<string, string[]> errors)
        : base(422, "One or more validation errors occurred.", "validation_failed")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : base(422, "One or more validation errors occurred.", "validation_failed")
    {
        Errors = new Dictionary<string, string[]>
        {
            { field, [error] }
        };
    }

    public ValidationException(string field, string error, string code)
        : this(field, error, code, "One or more validation errors occurred.")
    {
    }

    /// <summary>
    /// CARD-0666. For a failure whose only consumer may be <c>ex.Message</c> (a dispatch report),
    /// where the generic text would hide which value failed and why.
    /// </summary>
    public ValidationException(string field, string error, string code, string message)
        : base(422, message, code)
    {
        Errors = new Dictionary<string, string[]>
        {
            { field, [error] }
        };
    }
}

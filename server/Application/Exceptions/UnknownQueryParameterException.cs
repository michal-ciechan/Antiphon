namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0515 S3. A query key this route does not bind. Maps to HTTP 400
/// <c>unknown_query_parameter</c> so a typo cannot silently widen the result.
/// </summary>
public sealed class UnknownQueryParameterException : HttpException
{
    public UnknownQueryParameterException(string parameter, IReadOnlyList<string> supported)
        : base(
            400,
            $"Unknown query parameter '{parameter}'.",
            "unknown_query_parameter",
            new Dictionary<string, object?>
            {
                ["parameter"] = parameter,
                ["supported"] = supported,
            })
    {
    }
}

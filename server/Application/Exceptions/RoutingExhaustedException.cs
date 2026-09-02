using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0090: a create that delegated the choice to a chain found no available candidate, and
/// the caller asked for the 409 instead of a Blocked task. HTTP 409,
/// <c>code: routing_exhausted</c>, with a <c>complexityRouting</c> problem-details extension.
/// </summary>
public sealed class RoutingExhaustedException : HttpException
{
    public const string ErrorCode = "routing_exhausted";

    public RoutingExhaustedException(string message, ComplexityRoutingDto routing)
        : base(409, message, ErrorCode, new Dictionary<string, object?> { ["complexityRouting"] = routing })
    {
        Routing = routing;
    }

    public ComplexityRoutingDto Routing { get; }
}

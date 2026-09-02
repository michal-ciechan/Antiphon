using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0305: an explicit request disagrees with a <b>Required</b> routing pin. HTTP 409,
/// <c>code: routing_pin_conflict</c>, with a <c>routingPin</c> problem-details extension.
/// <c>ignoreRoutingPin: true</c> proceeds and leaves the pin standing (one-shot).
/// </summary>
public sealed class RoutingPinConflictException : HttpException
{
    public const string ErrorCode = "routing_pin_conflict";

    public RoutingPinConflictException(RoutingPinRefDto pin, string message)
        : base(409, message, ErrorCode, PinExtensions(pin))
    {
    }

    internal static IReadOnlyDictionary<string, object?> PinExtensions(RoutingPinRefDto pin) =>
        new Dictionary<string, object?> { ["routingPin"] = pin };
}

/// <summary>
/// CARD-0305: the resolved alias is on the stage-wide pin's <c>ForbiddenAliases</c> list. HTTP 409,
/// <c>code: routing_pin_forbidden</c>. A <b>Human</b> card pin is the deliberate exception and is
/// never checked against it (CARD-0301 pins fable while the Plan stage forbids fable).
/// </summary>
public sealed class RoutingPinForbiddenException : HttpException
{
    public const string ErrorCode = "routing_pin_forbidden";

    public RoutingPinForbiddenException(RoutingPinRefDto pin, string message)
        : base(409, message, ErrorCode, RoutingPinConflictException.PinExtensions(pin))
    {
    }
}

namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Who decided this routing pin (CARD-0305). The whole point of the column: a general policy
/// shift ("everything moves off fable") is an <see cref="Auto"/>-level change and must not
/// silently wash away a <see cref="Human"/> row the operator wrote for one card.
/// </summary>
public enum RoutingPinProvenance
{
    /// <summary>
    /// The orchestrator/dispatcher chose it (role default, quota fallback, "this looks cheap").
    /// Freely revised as conditions change.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// The operator said so explicitly. Sticky: an <see cref="Auto"/> write onto an active Human
    /// row is refused 409 <c>routing_pin_human</c>.
    /// </summary>
    Human = 1,
}

/// <summary>
/// How hard the pin binds (CARD-0305). <see cref="Preferred"/> yields to an explicit request and
/// records a warning; <see cref="Required"/> refuses it 409 <c>routing_pin_conflict</c> unless the
/// caller passes <c>ignoreRoutingPin</c>.
/// </summary>
public enum RoutingPinStrength
{
    Preferred = 0,
    Required = 1,
}

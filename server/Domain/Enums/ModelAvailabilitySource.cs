namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Who wrote the active <c>ModelAvailabilityHold</c> (CARD-0022 / CARD-0309). One active row
/// per (Kind, ModelAlias); Manual outranks AutoDetected on that key.
/// </summary>
public enum ModelAvailabilitySource
{
    /// <summary>CARD-0022: a parsed usage-limit Wall stub.</summary>
    AutoDetected = 0,

    /// <summary>CARD-0309: an operator PUT. Outranks AutoDetected on the same key.</summary>
    Manual = 1,
}

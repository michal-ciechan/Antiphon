namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Human rating of how much a card changes everything else. Integer order is higher = more, and
/// never appears on the wire — JSON uses the name. Default is <see cref="Normal"/>.
/// </summary>
public enum CardImportance
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Who produced <see cref="CardImportance"/> (CARD-0327). The automatic writer (tracker sync)
/// refreshes the value only while this is <see cref="Auto"/>; an explicit create/edit is
/// <see cref="Human"/> and is left standing. Same two-value shape as
/// <see cref="RoutingPinProvenance"/>.
/// </summary>
public enum CardImportanceProvenance
{
    /// <summary>A default or an automatic writer produced this value.</summary>
    Auto = 0,

    /// <summary>An explicit API content edit or create set it.</summary>
    Human = 1,
}

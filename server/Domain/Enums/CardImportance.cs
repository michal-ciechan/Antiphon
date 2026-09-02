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

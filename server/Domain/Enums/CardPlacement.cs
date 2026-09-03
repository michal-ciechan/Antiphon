namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Relative placement inside the card's current rank cell (CARD-0098). Used by
/// <c>PATCH /api/cards/{id}/position</c> with no axis change.
/// </summary>
public enum CardPlacement
{
    Top = 0,
    Bottom = 1
}

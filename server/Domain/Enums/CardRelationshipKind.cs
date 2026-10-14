namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// What a <c>CardRelationship</c> row means (CARD-0100). One member; the API takes the name as a
/// string and refuses any other (CardRelationshipApiTests.Unknown_kind_and_over_length_text_are_422).
/// </summary>
public enum CardRelationshipKind
{
    /// <summary><c>CardId</c> is blocked by <c>RelatedCardId</c>.</summary>
    BlockedBy = 0
}

using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0100 D-5 / A-3. Whether a blocker still holds its card. Resolved is a whitelist (archived,
/// Done, Canceled); any other status, including one this code does not know, keeps the blocker open
/// (CardRelationshipApiTests.Resolved_follows_the_blockers_Done_Canceled_archived_state_and_reopen).
/// </summary>
public static class CardBlockers
{
    public static bool IsResolved(CardStatus status, DateTime? archivedAt) =>
        archivedAt is not null || status is CardStatus.Done or CardStatus.Canceled;
}

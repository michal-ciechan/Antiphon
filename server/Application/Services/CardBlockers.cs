using System.Linq.Expressions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0100 D-5 / A-3. The one open-blocker predicate. Resolved is a whitelist (archived, Done,
/// Canceled); any other status keeps the blocker open.
/// </summary>
public static class CardBlockers
{
    /// <summary>A blocker card is resolved when it is archived or closed.</summary>
    public static bool IsResolved(CardStatus status, DateTime? archivedAt) =>
        throw new NotImplementedException();
}

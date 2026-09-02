using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The card-kind fire arm of CARD-0057. <see cref="CardService"/> is the production
/// implementation; tests inject a recording fake so Spawn never launches a real session.
/// </summary>
public interface IScheduledCardActions
{
    Task<bool> ApplyAutomatedMoveAsync(
        Guid cardId, CardStatus target, string reason, string movedBy, CancellationToken ct);

    Task<bool> ReleaseAutoDispatchHoldAsync(Guid cardId, string reason, string actor, CancellationToken ct);

    Task<SpawnCardResult> SpawnAsync(Guid cardId, SpawnCardRequest request, CancellationToken ct);
}

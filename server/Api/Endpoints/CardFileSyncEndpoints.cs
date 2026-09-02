using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// CARD-0004 S3: on-demand card → <c>docs/cards/&lt;slug&gt;/</c> reconcile for one board.
/// Mirrors <see cref="TrackerSyncEndpoints"/> (board-scoped POST, 409 on a concurrent run).
/// The tick is the fleet-wide equivalent; there is no <c>POST /api/card-files/sync</c> in v1.
/// </summary>
public static class CardFileSyncEndpoints
{
    public static void MapCardFileSyncEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/api/boards")
            .WithTags("Boards");

        boards.MapPost("/{id:guid}/card-files/sync", async (
            Guid id,
            CardTaskFileService sync,
            IOptions<CardFileSyncSettings> settings,
            CancellationToken cancellationToken,
            bool dryRun = false) =>
        {
            if (!settings.Value.Enabled)
            {
                throw new ConflictException(
                    "Card file sync is disabled.",
                    "card_file_sync_disabled");
            }

            var result = await sync.SyncBoardAsync(id, dryRun, cancellationToken);
            return Results.Ok(result);
        });
    }
}

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

        boards.MapGet("/{id:guid}/card-files/status", async (Guid id, CardTaskFileService sync, CancellationToken ct)
            => Results.Ok(await sync.GetStatusAsync(id, ct)));
        boards.MapPut("/{id:guid}/card-files/settings", async (Guid id, HttpContext http, CardTaskFileService sync,
            Antiphon.Server.Application.Interfaces.IEventBus events, CancellationToken ct) =>
        {
            var request = await CardFileRequestReader.ReadAsync<CardFileSettingsRequest>(http, ct);
            return Results.Ok(await sync.UpdateSettingsAsync(id, request.SyncCardFiles, request.ExpectedSyncCardFiles, events, ct));
        });

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

public sealed record CardFileSettingsRequest(bool? SyncCardFiles = null, bool? ExpectedSyncCardFiles = null);

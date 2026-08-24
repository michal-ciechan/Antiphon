using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// CARD-0166 S7: on-demand bidirectional tracker sync. Never called from the orchestrator tick.
/// </summary>
public static class TrackerSyncEndpoints
{
    public static void MapTrackerSyncEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/api/boards")
            .WithTags("Boards");

        boards.MapPost("/{id:guid}/tracker/sync", async (
            Guid id,
            AppDbContext db,
            TrackerBidirectionalSyncService sync,
            CancellationToken cancellationToken) =>
        {
            var board = await db.Boards.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Board), id);

            if (board.TrackerKind == TrackerKind.Internal)
            {
                throw new ConflictException(
                    "tracker block missing or inactive",
                    "tracker_inactive");
            }

            var result = await sync.RunAsync(id, cancellationToken);
            if (result.ConcurrentRunSkipped
                || result.Boards.Any(b => b.Skips.Contains("concurrent_run", StringComparer.Ordinal)))
            {
                throw new ConflictException(
                    "Sync already running for this board.",
                    "tracker_sync_running");
            }

            return Results.Ok(result);
        });

        app.MapPost("/api/tracker-sync/run", async (
            TrackerBidirectionalSyncService sync,
            CancellationToken cancellationToken) =>
        {
            var result = await sync.RunAsync(boardId: null, cancellationToken);
            return Results.Ok(result);
        }).WithTags("TrackerSync");
    }
}

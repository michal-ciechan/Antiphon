using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class CardEndpoints
{
    public static void MapCardEndpoints(this WebApplication app)
    {
        var cards = app.MapGroup("/api/cards")
            .WithTags("Cards");

        cards.MapPatch("/{id:guid}", async (
            Guid id,
            MoveCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.MoveAsync(id, request, cancellationToken));
        });

        cards.MapPost("/{id:guid}/spawn", async (
            Guid id,
            SpawnCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Accepted($"/api/cards/{id}", await service.SpawnAsync(id, request, cancellationToken));
        });
    }
}

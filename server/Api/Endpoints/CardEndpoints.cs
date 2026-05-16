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

        cards.MapGet("/{id:guid}/diff", async (
            Guid id,
            CardReviewService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetDiffAsync(id, cancellationToken));
        });

        cards.MapPost("/{id:guid}/comments", async (
            Guid id,
            CardCommentRequest request,
            CardReviewService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Accepted($"/api/cards/{id}", await service.PostCommentAsync(id, request, cancellationToken));
        });

        cards.MapPost("/{id:guid}/pr", async (
            Guid id,
            CardReviewService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.OpenPullRequestAsync(id, cancellationToken));
        });
    }
}

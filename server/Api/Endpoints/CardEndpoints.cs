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

        // Separate from PATCH /{id}, which is move-only: one verb that both moves and rewrites
        // invites partial-intent bugs, and the two have different concurrency stories.
        cards.MapPatch("/{id:guid}/content", async (
            Guid id,
            UpdateCardContentRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.UpdateContentAsync(id, request, cancellationToken));
        });

        cards.MapGet("/{id:guid}/revisions", async (
            Guid id,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetRevisionsAsync(id, cancellationToken));
        });

        cards.MapPost("/{id:guid}/spawn", async (
            Guid id,
            SpawnCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Accepted($"/api/cards/{id}", await service.SpawnAsync(id, request, cancellationToken));
        });

        // POST, not DELETE-with-a-body: a body on DELETE is hostile to proxies and some clients,
        // and this is not a delete — hard delete deliberately does not exist for cards.
        cards.MapPost("/{id:guid}/archive", async (
            Guid id,
            ArchiveCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.ArchiveAsync(id, request, cancellationToken));
        });

        cards.MapPost("/{id:guid}/unarchive", async (
            Guid id,
            UnarchiveCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.UnarchiveAsync(id, request, cancellationToken));
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

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class CardEndpoints
{
    /// <summary>
    /// Every route here takes <c>{id}</c> as a STRING, not <c>{id:guid}</c>, and resolves it
    /// through <see cref="CardService.ResolveCardIdAsync"/> first.
    /// </summary>
    /// <remarks>
    /// The precedent and the argument are <see cref="AgentTaskEndpoints"/>': the id a caller SEES
    /// must be the id the API TAKES. A card is called CARD-0051 in commit messages, docs, the UI
    /// and every conversation about it; its guid appears nowhere a human reads. A separate
    /// resolve-then-act endpoint would put back exactly the round trip this removes.
    ///
    /// <para>What the <c>:guid</c> constraint bought was an automatic 404 on garbage. The resolver
    /// answers 422 instead, naming the forms it accepts — the same protection with a message.</para>
    /// </remarks>
    public static void MapCardEndpoints(this WebApplication app)
    {
        var cards = app.MapGroup("/api/cards")
            .WithTags("Cards");

        // Registered before /{id} for readers, not for the router: a literal segment outranks a
        // route parameter regardless of order. If that ever stopped being true the resolver's 422
        // arm catches it — "limits" is not identifier-shaped — rather than reporting "no such card".
        cards.MapGet("/limits", () => Results.Ok(new CardLimitsDto(
            CardService.MaxTitleLength,
            CardService.MaxDescriptionLength,
            CardService.MaxReasonLength,
            CardService.MaxActorLength)));

        cards.MapGet("/{id}", async (
            string id,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.GetByIdAsync(cardId, cancellationToken));
        });

        // The whole of one piece of work: the card, its plans, its tasks and its commits. Read-only
        // and additive — no existing route's shape changes, and the assembly is a projection over
        // records that already exist rather than a new one (mobile-thread spec §D2).
        cards.MapGet("/{id}/thread", async (
            string id,
            CardService cardService,
            CardThreadService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await cardService.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.GetAsync(cardId, cancellationToken));
        });

        cards.MapPatch("/{id}", async (
            string id,
            MoveCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.MoveAsync(cardId, request, cancellationToken));
        });

        // Separate from PATCH /{id}, which is move-only: one verb that both moves and rewrites
        // invites partial-intent bugs, and the two have different concurrency stories.
        cards.MapPatch("/{id}/content", async (
            string id,
            UpdateCardContentRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.UpdateContentAsync(cardId, request, cancellationToken));
        });

        cards.MapGet("/{id}/revisions", async (
            string id,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.GetRevisionsAsync(cardId, cancellationToken));
        });

        cards.MapPost("/{id}/spawn", async (
            string id,
            SpawnCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Accepted(
                $"/api/cards/{cardId}", await service.SpawnAsync(cardId, request, cancellationToken));
        });

        // POST, not DELETE-with-a-body: a body on DELETE is hostile to proxies and some clients,
        // and this is not a delete — hard delete deliberately does not exist for cards.
        cards.MapPost("/{id}/archive", async (
            string id,
            ArchiveCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.ArchiveAsync(cardId, request, cancellationToken));
        });

        cards.MapPost("/{id}/unarchive", async (
            string id,
            UnarchiveCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.UnarchiveAsync(cardId, request, cancellationToken));
        });

        // Dedicated verb, not a move: Done/Canceled stay unreachable via PATCH /{id}.
        // Reopen never spawns — ApplyColumnMove has no spawn path.
        cards.MapPost("/{id}/reopen", async (
            string id,
            ReopenCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await service.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.ReopenAsync(cardId, request, cancellationToken));
        });

        cards.MapGet("/{id}/diff", async (
            string id,
            CardService cardService,
            CardReviewService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await cardService.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.GetDiffAsync(cardId, cancellationToken));
        });

        cards.MapPost("/{id}/comments", async (
            string id,
            CardCommentRequest request,
            CardService cardService,
            CardReviewService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await cardService.ResolveCardIdAsync(id, cancellationToken);
            return Results.Accepted(
                $"/api/cards/{cardId}", await service.PostCommentAsync(cardId, request, cancellationToken));
        });

        // CARD-0166: stored discussion thread. Deliberately not /comments — that route injects
        // into a live session (CardReviewService) and must keep that meaning.
        cards.MapGet("/{id}/discussion", async (
            string id,
            CardService cardService,
            CardCommentService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await cardService.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.ListAsync(cardId, cancellationToken));
        });

        cards.MapPost("/{id}/discussion", async (
            string id,
            CreateCardDiscussionRequest request,
            CardService cardService,
            CardCommentService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await cardService.ResolveCardIdAsync(id, cancellationToken);
            var created = await service.CreateAsync(cardId, request, cancellationToken);
            return Results.Created($"/api/cards/{cardId}/discussion", created);
        });

        cards.MapPost("/{id}/pr", async (
            string id,
            CardService cardService,
            CardReviewService service,
            CancellationToken cancellationToken) =>
        {
            var cardId = await cardService.ResolveCardIdAsync(id, cancellationToken);
            return Results.Ok(await service.OpenPullRequestAsync(cardId, cancellationToken));
        });
    }
}

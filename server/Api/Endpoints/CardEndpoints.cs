using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

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

        cards.MapGet("/", async (
            DateTime? updatedSince,
            CardStatus? status,
            Guid? boardId,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            if (updatedSince is null && status is null && boardId is null)
            {
                throw new BadRequestException(
                    "At least one of updatedSince, status, or boardId is required.");
            }

            return Results.Ok(await service.GetSummaryAsync(updatedSince, status, boardId, cancellationToken));
        });

        cards.MapGet("/{id}", async (
            string id,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.GetByIdAsync(cardId, cancellationToken));
        });

        // The whole of one piece of work: the card, its plans, its tasks and its commits. Read-only
        // and additive — no existing route's shape changes, and the assembly is a projection over
        // records that already exist rather than a new one (mobile-thread spec §D2).
        cards.MapGet("/{id}/thread", async (
            string id,
            HttpContext http,
            CardService cardService,
            CardThreadService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, cardService, tasks, cancellationToken);
            return Results.Ok(await service.GetAsync(cardId, cancellationToken));
        });

        cards.MapPatch("/{id}", async (
            string id,
            MoveCardRequest request,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.MoveAsync(cardId, request, cancellationToken));
        });

        // Separate from PATCH /{id}, which is move-only: one verb that both moves and rewrites
        // invites partial-intent bugs, and the two have different concurrency stories.
        cards.MapPatch("/{id}/content", async (
            string id,
            UpdateCardContentRequest request,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.UpdateContentAsync(cardId, request, cancellationToken));
        });

        cards.MapGet("/{id}/revisions", async (
            string id,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.GetRevisionsAsync(cardId, cancellationToken));
        });

        cards.MapPost("/{id}/spawn", async (
            string id,
            SpawnCardRequest request,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Accepted(
                $"/api/cards/{cardId}", await service.SpawnAsync(cardId, request, cancellationToken));
        });

        // POST, not DELETE-with-a-body: a body on DELETE is hostile to proxies and some clients,
        // and this is not a delete — hard delete deliberately does not exist for cards.
        cards.MapPost("/{id}/archive", async (
            string id,
            ArchiveCardRequest request,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.ArchiveAsync(cardId, request, cancellationToken));
        });

        cards.MapPost("/{id}/unarchive", async (
            string id,
            UnarchiveCardRequest request,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.UnarchiveAsync(cardId, request, cancellationToken));
        });

        // Dedicated verb, not a move: Done/Canceled stay unreachable via PATCH /{id}.
        // Reopen never spawns — ApplyColumnMove has no spawn path.
        cards.MapPost("/{id}/reopen", async (
            string id,
            ReopenCardRequest request,
            HttpContext http,
            CardService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, service, tasks, cancellationToken);
            return Results.Ok(await service.ReopenAsync(cardId, request, cancellationToken));
        });

        cards.MapGet("/{id}/diff", async (
            string id,
            HttpContext http,
            CardService cardService,
            CardReviewService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, cardService, tasks, cancellationToken);
            return Results.Ok(await service.GetDiffAsync(cardId, cancellationToken));
        });

        cards.MapPost("/{id}/comments", async (
            string id,
            CardCommentRequest request,
            HttpContext http,
            CardService cardService,
            CardReviewService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, cardService, tasks, cancellationToken);
            return Results.Accepted(
                $"/api/cards/{cardId}", await service.PostCommentAsync(cardId, request, cancellationToken));
        });

        // CARD-0166: stored discussion thread. Deliberately not /comments — that route injects
        // into a live session (CardReviewService) and must keep that meaning.
        cards.MapGet("/{id}/discussion", async (
            string id,
            HttpContext http,
            CardService cardService,
            CardCommentService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, cardService, tasks, cancellationToken);
            return Results.Ok(await service.ListAsync(cardId, cancellationToken));
        });

        cards.MapPost("/{id}/discussion", async (
            string id,
            CreateCardDiscussionRequest request,
            HttpContext http,
            CardService cardService,
            CardCommentService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, cardService, tasks, cancellationToken);
            var created = await service.CreateAsync(cardId, request, cancellationToken);
            return Results.Created($"/api/cards/{cardId}/discussion", created);
        });

        cards.MapPost("/{id}/pr", async (
            string id,
            HttpContext http,
            CardService cardService,
            CardReviewService service,
            AgentTaskService tasks,
            CancellationToken cancellationToken) =>
        {
            var cardId = await ResolveAsync(http, id, cardService, tasks, cancellationToken);
            return Results.Ok(await service.OpenPullRequestAsync(cardId, cancellationToken));
        });
    }

    /// <summary>
    /// Resolve a card route segment with the caller's scope. <c>cwd</c> is a disambiguation
    /// hint, never an authorisation — the only thing it can change is which of several cards
    /// the caller could already address by guid answers to a short name. It is read on writes
    /// as well as reads (<c>PATCH /api/cards/CARD-0011?boardId=…&amp;cwd=…</c>), because the
    /// script's writes are guid-addressed but a curl-by-hand move should have the same door.
    /// </summary>
    private static async Task<Guid> ResolveAsync(
        HttpContext http, string id, CardService cards, AgentTaskService tasks, CancellationToken ct)
    {
        var query = http.Request.Query;
        Guid? boardId = null;
        var boardRaw = query["boardId"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(boardRaw))
        {
            if (!Guid.TryParse(boardRaw, out var parsedBoard))
            {
                throw new ValidationException(
                    "boardId",
                    $"'{boardRaw}' is not a board id. Pass a guid.");
            }

            boardId = parsedBoard;
        }

        var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
        var scope = new CardScopeContext(
            boardId,
            caller?.Task?.CardId,
            caller?.SessionId,
            query["cwd"].FirstOrDefault() is { Length: > 0 } cwd ? cwd : caller?.WorkingDirectory);
        return await cards.ResolveCardIdAsync(id, scope, ct);
    }
}

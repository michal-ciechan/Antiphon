using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/api/boards")
            .WithTags("Boards");

        boards.MapGet("/", async (
            BoardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetAllAsync(cancellationToken));
        });

        // Archived cards are hidden by default and shown on request — archive is not deletion, so
        // the rows are always there to ask for.
        boards.MapGet("/{id:guid}", async (
            Guid id,
            BoardService service,
            CancellationToken cancellationToken,
            bool includeArchived = false,
            string view = "full") =>
        {
            var summary = string.Equals(view, "summary", StringComparison.OrdinalIgnoreCase);
            return Results.Ok(await service.GetByIdAsync(id, includeArchived, summary, cancellationToken));
        });

        // The board's shape without its contents: what a scripted move needs to turn a column NAME
        // into a column id, at a fraction of the full board payload.
        boards.MapGet("/{id:guid}/columns", async (
            Guid id,
            BoardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetColumnsAsync(id, cancellationToken));
        });

        boards.MapPost("/", async (
            CreateBoardRequest request,
            BoardService service,
            CancellationToken cancellationToken) =>
        {
            var board = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/boards/{board.Id}", board);
        });

        // Returns the result rather than 204: the caller needs to know whether the project went
        // with it, so the UI can navigate somewhere that still exists.
        boards.MapDelete("/{id:guid}", async (
            Guid id,
            BoardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.DeleteAsync(id, cancellationToken));
        });

        boards.MapPost("/{id:guid}/cards", async (
            Guid id,
            CreateCardRequest request,
            CardService service,
            CancellationToken cancellationToken) =>
        {
            var card = await service.CreateAsync(id, request, cancellationToken);
            return Results.Created($"/api/cards/{card.Id}", card);
        });

        boards.MapGet("/{id:guid}/workflow", async (
            Guid id,
            WorkflowDefinitionLoader loader,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await loader.GetAsync(id, cancellationToken));
        });

        boards.MapPut("/{id:guid}/workflow", async (
            Guid id,
            UpdateBoardWorkflowRequest request,
            WorkflowDefinitionLoader loader,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await loader.UpdateAsync(id, request, cancellationToken));
        });
    }
}

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

        boards.MapGet("/{id:guid}", async (
            Guid id,
            BoardService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetByIdAsync(id, cancellationToken));
        });

        boards.MapPost("/", async (
            CreateBoardRequest request,
            BoardService service,
            CancellationToken cancellationToken) =>
        {
            var board = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/boards/{board.Id}", board);
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
    }
}

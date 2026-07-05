using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class ChannelEndpoints
{
    public static void MapChannelEndpoints(this WebApplication app)
    {
        var channels = app.MapGroup("/api/channels")
            .WithTags("Channels");

        channels.MapGet("/", async (
            ChatChannelService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetAllAsync(cancellationToken));
        });

        channels.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateChatChannelRequest request,
            ChatChannelService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.UpdateAsync(id, request, cancellationToken));
        });
    }
}

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

        // CARD-0171: there is deliberately NO generic POST /{id}/send. ChatChannelService.SendAsync
        // is the primitive, and the server composes and addresses its own messages
        // (TrackerSyncNotifier). A text-to-any-channel megaphone with no audit row is a feature
        // that deserves its own card if it is ever wanted.
    }
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class DigestEndpoints
{
    public static void MapDigestEndpoints(this WebApplication app)
    {
        app.MapGet("/api/digest", async (AwayDigestProjection projection, DateTime? since, DateTime? until, CancellationToken ct) =>
        {
            var end = until ?? DateTime.UtcNow;
            return Results.Ok(await projection.ComputeAsync(since ?? end.AddHours(-24), end, ct));
        }).WithTags("Digest");
        app.MapPost("/api/digest/send", async (AwayDigestSendRequest? request, AwayDigestNotifier notifier, CancellationToken ct) =>
        {
            var results = await notifier.SendDueAsync(request?.ChannelId, request?.Since, force: true, ct);
            return Results.Ok(results);
        }).WithTags("Digest");
    }
}

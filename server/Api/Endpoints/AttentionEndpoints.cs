using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class AttentionEndpoints
{
    /// <summary>
    /// The one projection behind every "what needs a human" surface (CARD-0035 §D2). Deliberately
    /// FLEET-GLOBAL and unfiltered: stuckness is not a property of the project you happen to be
    /// looking at — a parked Telegram reply matters wherever the operator is standing.
    /// </summary>
    public static void MapAttentionEndpoints(this WebApplication app)
    {
        var attention = app.MapGroup("/api/attention").WithTags("Attention");

        attention.MapGet("/", async (
            AttentionService service,
            CancellationToken ct) => Results.Ok(await service.GetAsync(ct)));
    }
}

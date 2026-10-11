using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class AttentionEndpoints
{
    /// <summary>
    /// The one projection behind every "what needs a human" surface (CARD-0035 §D2). Deliberately
    /// FLEET-GLOBAL: stuckness is not a property of the project you happen to be looking at — a
    /// parked Telegram reply matters wherever the operator is standing. <c>minSeverity</c> is an
    /// optional floor (CARD-1085); there is still no board filter.
    /// </summary>
    public static void MapAttentionEndpoints(this WebApplication app)
    {
        var attention = app.MapGroup("/api/attention").WithTags("Attention");

        attention.MapGet("/", async (
            AttentionService service,
            string? minSeverity,
            CancellationToken ct) =>
        {
            // CARD-1085. Bind as text so an unknown floor is a 400 rather than a silent empty filter.
            AlertSeverity? floor = null;
            if (!string.IsNullOrWhiteSpace(minSeverity))
            {
                if (!Enum.TryParse<AlertSeverity>(minSeverity, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed))
                    return Results.BadRequest(new { error = "minSeverity_invalid" });
                floor = parsed;
            }

            return Results.Ok(await service.GetAsync(ct, minSeverity: floor));
        });

        attention.MapGet("/summary", async (
            AttentionService service,
            AttentionSummaryCache cache,
            CancellationToken ct) => Results.Ok(await cache.GetOrCreateAsync(() => service.GetSummaryAsync(ct))));
    }
}

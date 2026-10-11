using Antiphon.Server.Application.Exceptions;
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
            // CARD-1085. Omission keeps the unfiltered feed. A present value must be one of the
            // four names; empty, whitespace, numeric, and unknown values are a validation 400.
            AlertSeverity? floor = null;
            if (minSeverity is not null)
            {
                if (!IsAttentionSeverityName(minSeverity))
                    throw new BadRequestException(
                        "minSeverity must be Info, Warning, Error, or Critical.");
                floor = Enum.Parse<AlertSeverity>(minSeverity, ignoreCase: true);
            }

            return Results.Ok(await service.GetAsync(ct, minSeverity: floor));
        });

        attention.MapGet("/summary", async (
            AttentionService service,
            AttentionSummaryCache cache,
            CancellationToken ct) => Results.Ok(await cache.GetOrCreateAsync(() => service.GetSummaryAsync(ct))));
    }

    /// <summary>The four <see cref="AlertSeverity"/> names, case-insensitively, with no extra characters.</summary>
    private static bool IsAttentionSeverityName(string value)
    {
        foreach (var name in Enum.GetNames<AlertSeverity>())
        {
            if (value.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

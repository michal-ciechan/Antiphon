using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class SubscriptionUsageEndpoints
{
    /// <summary>
    /// Read-only projection of stored subscription-usage snapshots (CARD-0333 S1).
    /// Does not poll a provider, start a sweep, or mutate
    /// <c>SubscriptionUsageMonitoringSettings.Enabled</c>. No samples → empty array.
    /// </summary>
    public static void MapSubscriptionUsageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/subscription-usage").WithTags("SubscriptionUsage");

        group.MapGet("/", async (
            SubscriptionUsageReader reader,
            CancellationToken ct) =>
        {
            var snapshots = await reader.GetLatestAsync(ct);
            IReadOnlyList<SubscriptionUsageObservationDto> observations = snapshots
                .Select(s => new SubscriptionUsageObservationDto(
                    s.Provider,
                    s.PlanLabel,
                    s.RemainingPercent,
                    s.ResetsAt,
                    s.ObservedAt,
                    s.Age))
                .ToList();
            return Results.Ok(observations);
        });
    }
}

using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class StageOutcomeEndpoints
{
    /// <summary>
    /// Per-stage hit rate against cost (CARD-0272). HTTP, not SQL: the house front door is the API.
    /// </summary>
    public static void MapStageOutcomeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/stage-outcomes").WithTags("StageOutcomes");

        group.MapGet("/", async (
            DateTime? since,
            DateTime? until,
            string? stage,
            Guid? cardId,
            bool? latestOnly,
            StageOutcomeService outcomes,
            CancellationToken ct) =>
            Results.Ok(await outcomes.ListAsync(since, until, stage, cardId, latestOnly ?? true, ct)));
    }
}

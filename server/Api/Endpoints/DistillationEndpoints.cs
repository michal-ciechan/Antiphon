using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class DistillationEndpoints
{
    /// <summary>
    /// Read-only OutputDistillations ledger (CARD-0330 D7). One query burst each; no client work.
    /// </summary>
    public static void MapDistillationEndpoints(this WebApplication app)
    {
        var distillations = app.MapGroup("/api/distillations").WithTags("Distillations");

        distillations.MapGet("/", async (
            DateTime? since,
            DistillationOutcome? outcome,
            DistillationFeedback? feedback,
            int? limit,
            OutputDistillationService distiller,
            CancellationToken ct) =>
            Results.Ok(await distiller.ListAsync(since, outcome, feedback, limit, ct)));

        distillations.MapGet("/stats", async (
            DateTime? since,
            OutputDistillationService distiller,
            CancellationToken ct) =>
            Results.Ok(await distiller.StatsAsync(since, ct)));
    }
}

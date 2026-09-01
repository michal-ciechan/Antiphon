using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class ModelAvailabilityEndpoints
{
    /// <summary>
    /// Active holds + remaining aliases (CARD-0022 S4 GET; CARD-0309 PUT/DELETE Manual writer).
    /// Catch-all on alias so kind-wide <c>*</c> binds.
    /// </summary>
    public static void MapModelAvailabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/model-availability").WithTags("ModelAvailability");

        group.MapGet("/", async (
            ModelAvailability availability,
            CancellationToken ct) => Results.Ok(await availability.GetSnapshotAsync(ct)));

        group.MapPut("/{kind}/{*alias}", async (
            string kind,
            string alias,
            PutModelAvailabilityRequest? request,
            ModelAvailability availability,
            CancellationToken ct) =>
        {
            var hold = await availability.UpsertManualAsync(
                kind,
                alias,
                request?.DisabledUntil,
                request?.Reason,
                ct);
            return Results.Ok(hold);
        });

        group.MapDelete("/{kind}/{*alias}", async (
            string kind,
            string alias,
            ModelAvailability availability,
            CancellationToken ct) =>
        {
            await availability.ClearAsync(kind, alias, ct);
            return Results.NoContent();
        });
    }
}

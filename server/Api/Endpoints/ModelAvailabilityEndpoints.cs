using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class ModelAvailabilityEndpoints
{
    /// <summary>
    /// Thin read model of active holds + remaining aliases (CARD-0022 S4). Mapped next to
    /// <c>/api/agent-tasks/areas</c>. CARD-0309 later adds PUT/DELETE on this group.
    /// </summary>
    public static void MapModelAvailabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/model-availability").WithTags("ModelAvailability");

        group.MapGet("/", async (
            ModelAvailability availability,
            CancellationToken ct) => Results.Ok(await availability.GetSnapshotAsync(ct)));
    }
}

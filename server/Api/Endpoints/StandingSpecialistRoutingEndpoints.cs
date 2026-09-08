using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class StandingSpecialistRoutingEndpoints
{
    public static void MapStandingSpecialistRoutingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agents/{id:guid}/specialist-routing").WithTags("Agents");
        group.MapGet("/", async (Guid id, StandingSpecialistRoutingService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));
        group.MapPut("/", async (Guid id, PutStandingSpecialistRoutingRequest request,
            StandingSpecialistRoutingService service, CancellationToken ct) =>
            Results.Ok(await service.PutAsync(id, request, ct)));
        group.MapPost("/revalidate", async (Guid id, RevalidateStandingSpecialistRequest request,
            StandingSpecialistRoutingService service, CancellationToken ct) =>
            Results.Ok(await service.RevalidateAsync(id, request, ct)));
    }
}

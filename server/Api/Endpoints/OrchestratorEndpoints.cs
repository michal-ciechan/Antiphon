using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class OrchestratorEndpoints
{
    public static void MapOrchestratorEndpoints(this WebApplication app)
    {
        var orchestrator = app.MapGroup("/api/orchestrator")
            .WithTags("Orchestrator");

        orchestrator.MapGet("/state", async (
            OrchestratorService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetStateAsync(cancellationToken));
        });

        orchestrator.MapPost("/pause", (OrchestratorService service) =>
        {
            return Results.Ok(service.Pause());
        });

        orchestrator.MapPost("/resume", (OrchestratorService service) =>
        {
            return Results.Ok(service.Resume());
        });

        orchestrator.MapPost("/tick", async (
            OrchestratorService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.PollTickAsync(cancellationToken));
        });
    }
}

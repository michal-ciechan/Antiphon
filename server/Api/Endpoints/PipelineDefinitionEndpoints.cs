using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class PipelineDefinitionEndpoints
{
    public static void MapPipelineDefinitionEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/pipeline-definitions").WithTags("Pipeline definitions");

        routes.MapGet("/", async (PipelineDefinitionService service, CancellationToken ct,
            bool includeArchived = false) => Results.Ok(await service.ListAsync(includeArchived, ct)));

        routes.MapGet("/{id:guid}", async (Guid id, PipelineDefinitionService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)));

        routes.MapPost("/", async (CreatePipelineDefinitionRequest request,
            PipelineDefinitionService service, CancellationToken ct) =>
        {
            var definition = await service.CreateAsync(request, ct);
            return Results.Created($"/api/pipeline-definitions/{definition.Id}", definition);
        });

        routes.MapPost("/{id:guid}/clone", async (Guid id, ClonePipelineDefinitionRequest request,
            PipelineDefinitionService service, CancellationToken ct) =>
        {
            var definition = await service.CloneAsync(id, request.Name, ct);
            return Results.Created($"/api/pipeline-definitions/{definition.Id}", definition);
        });

        routes.MapPost("/{id:guid}/revisions", async (Guid id, AddPipelineDefinitionRevisionRequest request,
            PipelineDefinitionService service, CancellationToken ct) =>
            Results.Ok(await service.AddRevisionAsync(id, request, ct)));

        routes.MapPost("/{id:guid}/archive", async (Guid id, PipelineDefinitionArchiveRequest request,
            PipelineDefinitionService service, CancellationToken ct) =>
        {
            await service.ArchiveAsync(id, request, ct);
            return Results.NoContent();
        });

        routes.MapPost("/{id:guid}/unarchive", async (Guid id, PipelineDefinitionService service,
            CancellationToken ct) =>
        {
            await service.UnarchiveAsync(id, ct);
            return Results.NoContent();
        });
    }
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings/templates")
            .WithTags("Settings");

        group.MapGet("/", async (
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var templates = await service.GetAllAsync(cancellationToken);
            return Results.Ok(templates);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.GetByIdAsync(id, cancellationToken);
            return Results.Ok(template);
        });

        group.MapPost("/", async (
            CreateWorkflowTemplateRequest request,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/settings/templates/{template.Id}", template);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateWorkflowTemplateRequest request,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.UpdateAsync(id, request, cancellationToken);
            return Results.Ok(template);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });
    }
}

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        // --- Workflow Templates ---
        var templates = app.MapGroup("/api/settings/templates")
            .WithTags("Settings");

        templates.MapGet("/", async (
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAllAsync(cancellationToken);
            return Results.Ok(result);
        });

        templates.MapGet("/{id:guid}", async (
            Guid id,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.GetByIdAsync(id, cancellationToken);
            return Results.Ok(template);
        });

        templates.MapPost("/", async (
            CreateWorkflowTemplateRequest request,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/settings/templates/{template.Id}", template);
        });

        templates.MapPut("/{id:guid}", async (
            Guid id,
            UpdateWorkflowTemplateRequest request,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.UpdateAsync(id, request, cancellationToken);
            return Results.Ok(template);
        });

        templates.MapDelete("/{id:guid}", async (
            Guid id,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        // --- LLM Providers (Story 1.9) ---
        var providers = app.MapGroup("/api/settings/providers")
            .WithTags("Settings");

        providers.MapGet("/", async (
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAllAsync(cancellationToken);
            return Results.Ok(result);
        });

        providers.MapGet("/{id:guid}", async (
            Guid id,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var provider = await service.GetByIdAsync(id, cancellationToken);
            return Results.Ok(provider);
        });

        providers.MapPost("/", async (
            CreateLlmProviderRequest request,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var provider = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/settings/providers/{provider.Id}", provider);
        });

        providers.MapPut("/{id:guid}", async (
            Guid id,
            UpdateLlmProviderRequest request,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var provider = await service.UpdateAsync(id, request, cancellationToken);
            return Results.Ok(provider);
        });

        providers.MapDelete("/{id:guid}", async (
            Guid id,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        providers.MapPost("/{id:guid}/test", async (
            Guid id,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TestConnectivityAsync(id, cancellationToken);
            return Results.Ok(result);
        });

        // --- Model Routing (Story 1.9) ---
        var routing = app.MapGroup("/api/settings/model-routing")
            .WithTags("Settings");

        routing.MapGet("/", async (
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAllRoutingsAsync(cancellationToken);
            return Results.Ok(result);
        });

        routing.MapPost("/", async (
            CreateModelRoutingRequest request,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateRoutingAsync(request, cancellationToken);
            return Results.Created($"/api/settings/model-routing/{result.Id}", result);
        });

        routing.MapPut("/{id:guid}", async (
            Guid id,
            UpdateModelRoutingRequest request,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateRoutingAsync(id, request, cancellationToken);
            return Results.Ok(result);
        });

        routing.MapDelete("/{id:guid}", async (
            Guid id,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteRoutingAsync(id, cancellationToken);
            return Results.NoContent();
        });
    }
}

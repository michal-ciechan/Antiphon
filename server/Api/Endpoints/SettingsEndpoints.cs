using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

        // GET /{id}/stages — parse YAML and return a list of stage definitions
        templates.MapGet("/{id:guid}/stages", async (
            Guid id,
            WorkflowTemplateService service,
            CancellationToken cancellationToken) =>
        {
            var template = await service.GetByIdAsync(id, cancellationToken);
            var definition = WorkflowEngine.ParseYamlDefinition(template.YamlDefinition);
            var stageDtos = definition.Stages
                .Select(s => new StageDefinitionDto(
                    s.Name,
                    s.ExecutorType,
                    s.ModelName,
                    s.GateRequired,
                    s.SystemPrompt))
                .ToList();
            return Results.Ok(stageDtos);
        });

        // --- Template Groups ---
        var templateGroups = app.MapGroup("/api/settings/template-groups")
            .WithTags("Settings");

        templateGroups.MapGet("/", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var rows = await db.TemplateGroups
                .OrderBy(g => g.Name)
                .Select(g => new
                {
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsBuiltIn,
                    TemplateCount = g.Templates.Count(),
                    g.CreatedAt,
                    g.UpdatedAt,
                })
                .ToListAsync(cancellationToken);

            var groups = rows
                .Select(g => new TemplateGroupDto(
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsBuiltIn,
                    g.TemplateCount,
                    g.CreatedAt,
                    g.UpdatedAt))
                .ToList();

            return Results.Ok(groups);
        });

        templateGroups.MapGet("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var group = await db.TemplateGroups
                .Where(g => g.Id == id)
                .Select(g => new TemplateGroupDto(
                    g.Id,
                    g.Name,
                    g.Description,
                    g.IsBuiltIn,
                    g.Templates.Count,
                    g.CreatedAt,
                    g.UpdatedAt))
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(TemplateGroup), id);

            return Results.Ok(group);
        });

        templateGroups.MapPost("/", async (
            CreateTemplateGroupRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is required."]
                });
            }

            var duplicate = await db.TemplateGroups
                .AnyAsync(g => g.Name == request.Name, cancellationToken);

            if (duplicate)
            {
                throw new ConflictException(
                    $"A template group named '{request.Name}' already exists.");
            }

            var group = new TemplateGroup
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                IsBuiltIn = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.TemplateGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);

            var dto = new TemplateGroupDto(
                group.Id,
                group.Name,
                group.Description,
                group.IsBuiltIn,
                0,
                group.CreatedAt,
                group.UpdatedAt);

            return Results.Created($"/api/settings/template-groups/{group.Id}", dto);
        });

        templateGroups.MapPut("/{id:guid}", async (
            Guid id,
            UpdateTemplateGroupRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is required."]
                });
            }

            var group = await db.TemplateGroups
                .Include(g => g.Templates)
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(TemplateGroup), id);

            var duplicate = await db.TemplateGroups
                .AnyAsync(g => g.Name == request.Name && g.Id != id, cancellationToken);

            if (duplicate)
            {
                throw new ConflictException(
                    $"A template group named '{request.Name}' already exists.");
            }

            group.Name = request.Name;
            group.Description = request.Description;
            group.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            var dto = new TemplateGroupDto(
                group.Id,
                group.Name,
                group.Description,
                group.IsBuiltIn,
                group.Templates.Count(),
                group.CreatedAt,
                group.UpdatedAt);

            return Results.Ok(dto);
        });

        templateGroups.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var group = await db.TemplateGroups
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(TemplateGroup), id);

            if (group.IsBuiltIn)
            {
                throw new ConflictException("Built-in template groups cannot be deleted.");
            }

            // FK is set to SetNull so templates keep their data; just delete the group
            db.TemplateGroups.Remove(group);
            await db.SaveChangesAsync(cancellationToken);

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

        // Per-template model routing
        templates.MapGet("/{id:guid}/model-routing", async (
            Guid id,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetRoutingsByTemplateAsync(id, cancellationToken);
            return Results.Ok(result);
        });

        templates.MapPost("/{id:guid}/model-routing", async (
            Guid id,
            CreateModelRoutingRequest request,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateRoutingAsync(id, request, cancellationToken);
            return Results.Created($"/api/settings/model-routing/{result.Id}", result);
        });

        templates.MapPut("/model-routing/{routingId:guid}", async (
            Guid routingId,
            UpdateModelRoutingRequest request,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateRoutingAsync(routingId, request, cancellationToken);
            return Results.Ok(result);
        });

        templates.MapDelete("/model-routing/{routingId:guid}", async (
            Guid routingId,
            LlmProviderService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteRoutingAsync(routingId, cancellationToken);
            return Results.NoContent();
        });
    }
}

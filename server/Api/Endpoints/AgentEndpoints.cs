using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        var agents = app.MapGroup("/api/agents")
            .WithTags("Agents");

        agents.MapGet("/", async (
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetAllAsync(cancellationToken));
        });

        agents.MapGet("/definitions", (AgentRegistry registry) =>
        {
            var settings = registry.Settings;
            var definitions = settings.Definitions
                .OrderBy(kvp => kvp.Key)
                .Select(kvp =>
                {
                    var kind = Enum.TryParse<AgentKind>(kvp.Value.Kind, ignoreCase: true, out var parsed)
                        ? parsed
                        : AgentKind.Raw;
                    return new AgentDefinitionDto(
                        kvp.Key,
                        kind,
                        string.Equals(kvp.Key, settings.DefaultDefinition, StringComparison.Ordinal));
                })
                .ToList();

            return Results.Ok(new AgentRegistryDto(settings.DefaultDefinition, definitions));
        });

        agents.MapGet("/{id:guid}", async (
            Guid id,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetByIdAsync(id, cancellationToken));
        });

        agents.MapPost("/", async (
            CreateAgentRequest request,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            var agent = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/agents/{agent.Id}", agent);
        });

        agents.MapPost("/draft", async (
            DraftAgentRequest request,
            AgentDraftService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.DraftAsync(request, cancellationToken));
        });

        agents.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateAgentRequest request,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.UpdateAsync(id, request, cancellationToken));
        });

        agents.MapPost("/{id:guid}/queue", async (
            Guid id,
            AssignAgentCardRequest request,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.AssignCardAsync(id, request, cancellationToken));
        });

        agents.MapPatch("/{id:guid}/queue", async (
            Guid id,
            ReorderAgentQueueRequest request,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.ReorderQueueAsync(id, request, cancellationToken));
        });

        agents.MapDelete("/{id:guid}/queue/{cardId:guid}", async (
            Guid id,
            Guid cardId,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            await service.RemoveCardAsync(id, cardId, cancellationToken);
            return Results.NoContent();
        });
    }
}

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

        // The channel preamble preset for the given provider — the template the UI's "Use preset"
        // button drops into the SystemPromptAppend textarea ({agentName}/{channels} placeholders
        // render at launch time).
        agents.MapGet("/preamble-preset", (string? provider) =>
        {
            var requestedProvider = provider ?? "telegram";
            var template = ChannelPreamble.PresetTemplateFor(requestedProvider);
            return template is not null
                ? Results.Ok(new PreamblePresetDto(template))
                : Results.NotFound(new { error = $"No preamble preset for provider '{provider}'." });
        });

        // The instruction bundles an operator may attach to an agent (CARD-0058 slice 6). Read-only
        // and unauthenticated-by-the-same-rules as /definitions: the catalog is CODE — markdown
        // files under server/Bundles/, embedded in this assembly and versioned by content hash — so
        // there is nothing here to write. What an operator chooses is which agent carries which key,
        // and that rides UpdateAgentRequest.BundleKeys with the rest of the agent's settings rather
        // than a second write path that could disagree with it.
        //
        // Reply-style bundles are deliberately absent: the ReplyStyle dropdown already picks one,
        // and a second control that could contradict it would give an agent two voices.
        agents.MapGet("/bundles", () =>
        {
            var catalog = InstructionBundles.Attachable
                .Select(b => new InstructionBundleDto(b.Key, b.Version, b.Stamp, b.Summary, b.Text.Length))
                .ToList();
            return Results.Ok(catalog);
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

        agents.MapDelete("/{id:guid}", async (
            Guid id,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        agents.MapGet("/{id:guid}/incidents", async (
            Guid id,
            int? take,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetIncidentsAsync(id, take ?? 50, cancellationToken));
        });

        agents.MapPost("/{id:guid}/start", async (
            Guid id,
            StartAgentRequest request,
            AgentControlService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.StartAsync(id, request, cancellationToken));
        });

        agents.MapPost("/{id:guid}/stop", async (
            Guid id,
            AgentControlService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.StopAsync(id, cancellationToken));
        });

        agents.MapPost("/{id:guid}/attach-herdr", async (
            Guid id,
            AttachHerdrPaneRequest request,
            AgentControlService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.AttachHerdrAsync(id, request, cancellationToken));
        });

        // CARD-0214. Creates the agent's configured working directory (the readiness
        // panel's create-directory fix action). No body: the path is never caller-supplied.
        agents.MapPost("/{id:guid}/ensure-directory", async (
            Guid id,
            AgentService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.EnsureWorkingDirectoryAsync(id, cancellationToken));
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

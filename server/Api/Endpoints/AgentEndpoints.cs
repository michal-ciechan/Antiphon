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
    }
}

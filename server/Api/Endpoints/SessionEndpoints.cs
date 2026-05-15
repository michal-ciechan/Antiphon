using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.Pty;

namespace Antiphon.Server.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var sessions = app.MapGroup("/api/sessions")
            .WithTags("Sessions");

        sessions.MapPost("/", async (
            StartAgentSessionRequest request,
            AgentRegistry registry,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            ValidateTerminalSize(request.Cols, request.Rows);
            var definitionName = string.IsNullOrWhiteSpace(request.DefinitionName)
                ? registry.Settings.DefaultDefinition
                : request.DefinitionName;
            var spec = registry.Resolve(definitionName, new AgentLaunchOptions(
                Cwd: null,
                Cols: request.Cols,
                Rows: request.Rows,
                ExtraArgs: request.ExtraArgs,
                ExtraEnv: request.ExtraEnv));
            var result = await service.StartAsync(
                request with { DefinitionName = definitionName, AgentKind = spec.Kind },
                spec,
                cancellationToken);

            return Results.Created($"/api/sessions/{result.SessionId}", result);
        });

        sessions.MapGet("/{id:guid}/buffer", async (
            Guid id,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            var buffer = await service.GetBufferAsync(id, cancellationToken);
            return Results.Ok(new AgentSessionBufferDto(id, buffer));
        });

        sessions.MapPost("/{id:guid}/input", async (
            Guid id,
            SendSessionInputRequest request,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            await service.SendInputAsync(id, request.Input, cancellationToken);
            return Results.NoContent();
        });

        sessions.MapPost("/{id:guid}/resize", async (
            Guid id,
            ResizeSessionRequest request,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            ValidateTerminalSize(request.Cols, request.Rows);
            await service.ResizeAsync(id, request.Cols, request.Rows, cancellationToken);
            return Results.NoContent();
        });

        sessions.MapPost("/{id:guid}/kill", async (
            Guid id,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            await service.KillAsync(id, cancellationToken);
            return Results.NoContent();
        });
    }

    private static void ValidateTerminalSize(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
            throw new ValidationException("size", "Terminal cols and rows must be positive.");
    }
}

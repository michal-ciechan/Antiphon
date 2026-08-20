using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// CRUD for stored API keys at both scopes (CARD-0106 S1).
///
/// <para>There is deliberately NO endpoint that returns a key's value. Writes are the only way a
/// value moves in, and <c>ApiKeyEnvResolver</c> at launch is the only way one moves out — the same
/// write-only contract the agent-TUI managed secrets already have.</para>
/// </summary>
public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this WebApplication app)
    {
        var keys = app.MapGroup("/api/api-keys").WithTags("API Keys");

        // Every key in the installation, metadata only. The scope is on each row (projectId null =
        // global), so a UI that wants one scope filters this rather than needing its own route.
        keys.MapGet("/", async (
            ApiKeyService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        keys.MapGet("/global", async (
            ApiKeyService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListGlobalAsync(cancellationToken)));

        // Upsert. The name is in the path because it is the key's identity to an operator; the id
        // is an implementation detail they never type.
        keys.MapPut("/{name}", async (
            string name,
            PutApiKeyRequest request,
            ApiKeyService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.PutAsync(
                name,
                request.ProjectId,
                request.Value,
                cancellationToken)));

        keys.MapDelete("/{id:guid}", async (
            Guid id,
            ApiKeyService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        var projectKeys = app.MapGroup("/api/projects/{projectId:guid}/api-keys")
            .WithTags("API Keys");

        // This project's OWN keys — not the globals it also resolves against. The project settings
        // panel edits exactly this list.
        projectKeys.MapGet("/", async (
            Guid projectId,
            ApiKeyService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListForProjectAsync(projectId, cancellationToken)));

        // The scope comes from the route, so a project-scoped write cannot land globally because a
        // caller forgot the body field.
        projectKeys.MapPut("/{name}", async (
            Guid projectId,
            string name,
            PutApiKeyRequest request,
            ApiKeyService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.PutAsync(
                name,
                projectId,
                request.Value,
                cancellationToken)));
    }
}

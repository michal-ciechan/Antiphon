using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        var projects = app.MapGroup("/api/projects")
            .WithTags("Projects");

        projects.MapGet("/", async (
            ProjectService service,
            CancellationToken cancellationToken,
            bool includeArchived = false) =>
        {
            var result = await service.GetAllAsync(includeArchived, cancellationToken);
            return Results.Ok(result);
        });

        projects.MapGet("/{id:guid}", async (
            Guid id,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            var project = await service.GetByIdAsync(id, cancellationToken);
            return Results.Ok(project);
        });

        projects.MapGet("/{id:guid}/readiness", async (
            Guid id,
            ProjectSetupService service,
            CancellationToken cancellationToken) =>
        {
            var readiness = await service.GetReadinessAsync(id, cancellationToken);
            return Results.Ok(readiness);
        });

        projects.MapPost("/{id:guid}/acknowledge-orchestrator-workspace", async (
            Guid id,
            ProjectSetupService service,
            CancellationToken cancellationToken) =>
        {
            var readiness = await service.AcknowledgeOrchestratorWorkspaceAsync(id, cancellationToken);
            return Results.Ok(readiness);
        });

        projects.MapGet("/readiness", async (
            string? ids,
            ProjectSetupService service,
            CancellationToken cancellationToken) =>
        {
            var projectIds = ParseReadinessIds(ids);
            var readiness = await service.GetReadinessBatchAsync(projectIds, cancellationToken);
            return Results.Ok(readiness);
        });

        projects.MapGet("/setup-catalog", async (
            ProjectSetupService service,
            CancellationToken cancellationToken) =>
        {
            var catalog = await service.GetCatalogAsync(cancellationToken);
            return Results.Ok(catalog);
        });

        projects.MapPost("/setup", async (
            ProjectSetupRequest request,
            ProjectSetupService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SetupAsync(request, cancellationToken);
            return Results.Created($"/api/projects/{result.Project.Id}", result);
        });

        projects.MapPost("/", async (
            CreateProjectRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            var project = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/projects/{project.Id}", project);
        });

        projects.MapPut("/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            var project = await service.UpdateAsync(id, request, cancellationToken);
            return Results.Ok(project);
        });

        projects.MapGet("/{id:guid}/deletion-impact", async (
            Guid id,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            var impact = await service.GetDeletionImpactAsync(id, cancellationToken);
            return Results.Ok(impact);
        });

        // force=true is the caller confirming the impact report. Without it a project that owns
        // boards or cards answers 409 rather than destroying them (or, as it once did, 500).
        projects.MapDelete("/{id:guid}", async (
            Guid id,
            ProjectService service,
            CancellationToken cancellationToken,
            bool force = false) =>
        {
            await service.DeleteAsync(id, force, cancellationToken);
            return Results.NoContent();
        });

        projects.MapPost("/test-connectivity", async (
            TestGitConnectivityRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TestGitConnectivityAsync(request.GitRepositoryUrl, cancellationToken);
            return Results.Ok(result);
        });

        // POST, not DELETE-with-a-body: a body on DELETE is hostile to proxies and some clients,
        // and this is not a delete — hard delete remains the existing DELETE /{id}.
        projects.MapPost("/{id:guid}/archive", async (
            Guid id,
            ArchiveProjectRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.ArchiveAsync(id, request, cancellationToken));
        });

        projects.MapPost("/{id:guid}/unarchive", async (
            Guid id,
            UnarchiveProjectRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.UnarchiveAsync(id, request, cancellationToken));
        });
    }

    private static List<Guid> ParseReadinessIds(string? ids)
    {
        var values = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length > 100)
            throw new Antiphon.Server.Application.Exceptions.ValidationException(
                "ids", "At most 100 project ids may be requested at once.");

        var parsed = new List<Guid>(values.Length);
        foreach (var value in values)
        {
            if (!Guid.TryParse(value, out var id))
                throw new Antiphon.Server.Application.Exceptions.ValidationException(
                    "ids", "ids must be a comma-separated list of project ids.");
            parsed.Add(id);
        }
        return parsed;
    }
}

public record TestGitConnectivityRequest(string GitRepositoryUrl);

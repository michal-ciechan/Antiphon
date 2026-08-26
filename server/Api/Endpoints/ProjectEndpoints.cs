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
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAllAsync(cancellationToken);
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
    }
}

public record TestGitConnectivityRequest(string GitRepositoryUrl);

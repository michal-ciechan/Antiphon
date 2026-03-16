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

        projects.MapDelete("/{id:guid}", async (
            Guid id,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
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

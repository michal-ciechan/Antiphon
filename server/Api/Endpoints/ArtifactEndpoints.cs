using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class ArtifactEndpoints
{
    public static void MapArtifactEndpoints(this WebApplication app)
    {
        var artifacts = app.MapGroup("/api/workflows/{id:guid}/artifacts")
            .WithTags("Artifacts");

        // GET /api/workflows/{id}/artifacts — list all artifacts for this workflow
        artifacts.MapGet("/", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .Include(w => w.Stages.OrderBy(s => s.StageOrder))
                    .ThenInclude(s => s.StageExecutions)
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            // Build artifact list from completed stage executions with git tags
            var artifactDtos = workflow.Stages
                .Where(s => s.Status == StageStatus.Completed)
                .SelectMany(stage => stage.StageExecutions
                    .Where(e => e.Status == StageStatus.Completed && !string.IsNullOrEmpty(e.GitTagName))
                    .Select(execution => new ArtifactListItemDto(
                        execution.Id,
                        stage.Id,
                        stage.Name,
                        $"{stage.Name}-v{execution.Version}.md",
                        execution.Version,
                        stage.StageOrder == 0, // First stage artifact is primary
                        execution.CompletedAt ?? execution.StartedAt)))
                .OrderBy(a => a.StageName)
                .ThenByDescending(a => a.Version)
                .ToList();

            return Results.Ok(artifactDtos);
        });

        // GET /api/workflows/{id}/artifacts/{stageId} — get latest artifact content for a stage
        artifacts.MapGet("/{stageId:guid}", async (
            Guid id,
            Guid stageId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var stage = await db.Stages
                .Include(s => s.StageExecutions)
                .FirstOrDefaultAsync(s => s.Id == stageId && s.WorkflowId == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Stage), stageId);

            var latestExecution = stage.StageExecutions
                .Where(e => e.Status == StageStatus.Completed)
                .OrderByDescending(e => e.Version)
                .FirstOrDefault()
                ?? throw new NotFoundException("Completed execution", stageId);

            var dto = new ArtifactDetailDto(
                latestExecution.Id,
                stage.Id,
                stage.Name,
                $"{stage.Name}-v{latestExecution.Version}.md",
                latestExecution.GitTagName ?? string.Empty,
                latestExecution.Version,
                stage.StageOrder == 0,
                latestExecution.CompletedAt ?? latestExecution.StartedAt);

            return Results.Ok(dto);
        });
    }
}

public record ArtifactListItemDto(
    Guid Id,
    Guid StageId,
    string StageName,
    string FileName,
    int Version,
    bool IsPrimary,
    DateTime CreatedAt);

public record ArtifactDetailDto(
    Guid Id,
    Guid StageId,
    string StageName,
    string FileName,
    string Content,
    int Version,
    bool IsPrimary,
    DateTime CreatedAt);

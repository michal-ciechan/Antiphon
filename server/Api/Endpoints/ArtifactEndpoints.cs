using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
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
                    .Where(e => e.Status == StageStatus.Completed && (!string.IsNullOrEmpty(e.GitTagName) || !string.IsNullOrEmpty(e.OutputContent)))
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

        // GET /api/workflows/{id}/artifacts/{stageExecutionId}/section-reviews — list reviews for an execution
        artifacts.MapGet("/{stageExecutionId:guid}/section-reviews", async (
            Guid id,
            Guid stageExecutionId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var reviews = await db.ArtifactSectionReviews
                .Where(r => r.StageExecutionId == stageExecutionId)
                .OrderBy(r => r.SectionPath)
                .Select(r => new SectionReviewDto(r.Id, r.StageExecutionId, r.SectionPath, r.ContentHash, r.ReviewedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(reviews);
        });

        // POST /api/workflows/{id}/artifacts/{stageExecutionId}/section-reviews — upsert a review
        artifacts.MapPost("/{stageExecutionId:guid}/section-reviews", async (
            Guid id,
            Guid stageExecutionId,
            UpsertSectionReviewRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var existing = await db.ArtifactSectionReviews
                .FirstOrDefaultAsync(
                    r => r.StageExecutionId == stageExecutionId && r.SectionPath == request.SectionPath,
                    cancellationToken);

            if (existing is null)
            {
                existing = new ArtifactSectionReview
                {
                    Id = Guid.NewGuid(),
                    StageExecutionId = stageExecutionId,
                    SectionPath = request.SectionPath,
                    ContentHash = request.ContentHash,
                    ReviewedAt = DateTime.UtcNow,
                };
                db.ArtifactSectionReviews.Add(existing);
            }
            else
            {
                existing.ContentHash = request.ContentHash;
                existing.ReviewedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new SectionReviewDto(
                existing.Id,
                existing.StageExecutionId,
                existing.SectionPath,
                existing.ContentHash,
                existing.ReviewedAt));
        });

        // DELETE /api/workflows/{id}/artifacts/{stageExecutionId}/section-reviews/{sectionPath} — unmark reviewed
        artifacts.MapDelete("/{stageExecutionId:guid}/section-reviews/{**sectionPath}", async (
            Guid id,
            Guid stageExecutionId,
            string sectionPath,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var review = await db.ArtifactSectionReviews
                .FirstOrDefaultAsync(
                    r => r.StageExecutionId == stageExecutionId && r.SectionPath == sectionPath,
                    cancellationToken);

            if (review is not null)
            {
                db.ArtifactSectionReviews.Remove(review);
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.NoContent();
        });

        // GET /api/workflows/{id}/artifacts/{stageId}?version={version} — get artifact content for a stage
        artifacts.MapGet("/{stageId:guid}", async (
            Guid id,
            Guid stageId,
            int? version,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .Include(w => w.Project)
                .Include(w => w.Stages)
                    .ThenInclude(s => s.StageExecutions)
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            var stage = workflow.Stages.FirstOrDefault(s => s.Id == stageId)
                ?? throw new NotFoundException(nameof(Stage), stageId);

            var completedExecutions = stage.StageExecutions
                .Where(e => e.Status == StageStatus.Completed);

            var execution = version.HasValue
                ? completedExecutions.FirstOrDefault(e => e.Version == version.Value)
                    ?? throw new NotFoundException($"Completed execution v{version.Value}", stageId)
                : completedExecutions
                    .OrderByDescending(e => e.Version)
                    .FirstOrDefault()
                    ?? throw new NotFoundException("Completed execution", stageId);

            var localRepoPath = workflow.Project?.LocalRepositoryPath;
            var artifactFileName = $"{stage.Name}-v{execution.Version}.md";

            string content;
            if (!string.IsNullOrEmpty(localRepoPath))
            {
                var artifactDir = GitService.GetArtifactDirectory(id);
                var filePath = Path.Combine(
                    localRepoPath,
                    artifactDir,
                    artifactFileName
                );
                content = File.Exists(filePath)
                    ? await File.ReadAllTextAsync(filePath, cancellationToken)
                    : execution.OutputContent ?? string.Empty;  // fall back to stored content
            }
            else
            {
                content = execution.OutputContent ?? string.Empty;
            }

            var dto = new ArtifactDetailDto(
                execution.Id,
                stage.Id,
                stage.Name,
                artifactFileName,
                content,
                execution.Version,
                stage.StageOrder == 0,
                execution.CompletedAt ?? execution.StartedAt);

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

public record SectionReviewDto(
    Guid Id,
    Guid StageExecutionId,
    string SectionPath,
    string ContentHash,
    DateTime ReviewedAt);

public record UpsertSectionReviewRequest(string SectionPath, string ContentHash);

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed record ResolvedPipeline(
    PipelineDefinition Definition, PipelineDefinitionRevision Revision, string Source)
{
    public PipelineResolutionDto ToDto() =>
        new(Definition.Id, Definition.Name, Revision.Id, Revision.RevisionNumber,
            Revision.ContentHash, Source);
}

public sealed class PipelineResolution(AppDbContext db)
{
    public async Task<ResolvedPipeline> ResolveForCardAsync(Card card, CancellationToken ct)
    {
        var board = await db.Boards.Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == card.BoardId, ct)
            ?? throw new NotFoundException(nameof(Board), card.BoardId);
        return await ResolveAsync(board.PipelineDefinitionId,
            board.Project.DefaultPipelineDefinitionId, ct)
            ?? throw new ConflictException("The standard pipeline has not been seeded.", "pipeline_definition_missing");
    }

    public async Task<PipelineResolutionDto?> ForBoardAsync(Board board, CancellationToken ct)
    {
        var projectId = board.Project.Id != Guid.Empty
            ? board.Project.DefaultPipelineDefinitionId
            : await db.Projects.Where(p => p.Id == board.ProjectId)
                .Select(p => p.DefaultPipelineDefinitionId).SingleAsync(ct);
        return (await ResolveAsync(board.PipelineDefinitionId, projectId, ct))?.ToDto();
    }

    public async Task<PipelineResolutionDto?> ForProjectAsync(Project project, CancellationToken ct) =>
        (await ResolveAsync(null, project.DefaultPipelineDefinitionId, ct))?.ToDto();

    private async Task<ResolvedPipeline?> ResolveAsync(Guid? boardId, Guid? projectId, CancellationToken ct)
    {
        var id = boardId ?? projectId ?? PipelineDefinitions.StandardPipelineId;
        var definition = await db.PipelineDefinitions.AsNoTracking().Include(d => d.ActiveRevision)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
        {
            if (boardId is null && projectId is null) return null;
            throw new NotFoundException(nameof(PipelineDefinition), id);
        }
        var revision = definition.ActiveRevision
            ?? throw new ConflictException("Pipeline definition has no active revision.", "pipeline_revision_missing");
        return new ResolvedPipeline(definition, revision,
            boardId is not null ? "board" : projectId is not null ? "project" : "builtin");
    }
}

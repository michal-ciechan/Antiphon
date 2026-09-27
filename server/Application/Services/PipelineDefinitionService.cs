using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class PipelineDefinitionService(AppDbContext db, TimeProvider time)
{
    public async Task<IReadOnlyList<PipelineDefinitionDto>> ListAsync(bool includeArchived, CancellationToken ct)
    {
        var definitions = await db.PipelineDefinitions.AsNoTracking()
            .Include(d => d.ActiveRevision)
            .Where(d => includeArchived || d.ArchivedAt == null)
            .OrderBy(d => d.Name).ToListAsync(ct);
        return definitions.Select(ToDto).ToList();
    }

    public async Task<PipelineDefinitionDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await LoadAsync(id, ct));

    public async Task<PipelineDefinitionDto> CreateAsync(
        CreatePipelineDefinitionRequest request, CancellationToken ct)
    {
        var name = ValidateName(request.Name);
        ValidateDescription(request.Description);
        ValidateStages(request.Stages);
        if (await db.PipelineDefinitions.AnyAsync(d => d.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException($"Pipeline definition '{name}' already exists.", "pipeline_definition_name_taken");

        var now = time.GetUtcNow().UtcDateTime;
        var definition = new PipelineDefinition
        {
            Id = Guid.NewGuid(), Name = name, Description = request.Description.Trim(),
            Source = PipelineDefinitionSource.Custom, CreatedAt = now, UpdatedAt = now
        };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.PipelineDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        var revision = NewRevision(definition.Id, 1, request.Stages, null, now);
        db.PipelineDefinitionRevisions.Add(revision);
        await db.SaveChangesAsync(ct);
        definition.ActiveRevisionId = revision.Id;
        definition.ActiveRevision = revision;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(definition);
    }

    public async Task<PipelineDefinitionDto> CloneAsync(Guid id, string name, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        var revision = source.ActiveRevision ?? throw new ConflictException("Pipeline definition has no active revision.", "pipeline_revision_missing");
        return await CreateAsync(new CreatePipelineDefinitionRequest(
            name, source.Description, PipelineStagesJson.Parse(revision.StagesJson)), ct);
    }

    public async Task<PipelineDefinitionDto> AddRevisionAsync(
        Guid id, AddPipelineDefinitionRevisionRequest request, CancellationToken ct)
    {
        ValidateStages(request.Stages);
        if (request.ChangeNote?.Length > 400)
            throw new ValidationException("changeNote", "Change note must be at most 400 characters.", "pipeline_revision_note_too_long");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);
        var definition = await LoadAsync(id, ct);
        RefuseBuiltIn(definition);
        var number = await db.PipelineDefinitionRevisions
            .Where(r => r.DefinitionId == id).MaxAsync(r => (int?)r.RevisionNumber, ct) ?? 0;
        var revision = NewRevision(id, number + 1, request.Stages, request.ChangeNote, time.GetUtcNow().UtcDateTime);
        db.PipelineDefinitionRevisions.Add(revision);
        await db.SaveChangesAsync(ct);
        definition.ActiveRevisionId = revision.Id;
        definition.ActiveRevision = revision;
        definition.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(definition);
    }

    public async Task ArchiveAsync(Guid id, PipelineDefinitionArchiveRequest request, CancellationToken ct)
    {
        var definition = await LoadAsync(id, ct);
        RefuseBuiltIn(definition);
        definition.ArchivedAt = time.GetUtcNow().UtcDateTime;
        definition.ArchivedReason = request.Reason;
        definition.ArchivedBy = request.Actor;
        definition.UpdatedAt = definition.ArchivedAt.Value;
        await db.SaveChangesAsync(ct);
    }

    public async Task UnarchiveAsync(Guid id, CancellationToken ct)
    {
        var definition = await LoadAsync(id, ct);
        RefuseBuiltIn(definition);
        definition.ArchivedAt = null;
        definition.ArchivedReason = null;
        definition.ArchivedBy = null;
        definition.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task ValidatePointerAsync(Guid? id, CancellationToken ct)
    {
        if (id is null) return;
        var definition = await LoadAsync(id.Value, ct);
        if (definition.ArchivedAt is not null)
            throw new ConflictException($"Pipeline definition '{definition.Name}' is archived.", "pipeline_definition_archived");
    }

    private async Task<PipelineDefinition> LoadAsync(Guid id, CancellationToken ct) =>
        await db.PipelineDefinitions.Include(d => d.ActiveRevision)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException(nameof(PipelineDefinition), id);

    private static PipelineDefinitionRevision NewRevision(
        Guid id, int number, IReadOnlyList<PipelineStageSpec> stages, string? note, DateTime now)
    {
        var json = PipelineStagesJson.Serialise(stages);
        return new PipelineDefinitionRevision
        {
            Id = Guid.NewGuid(), DefinitionId = id, RevisionNumber = number,
            StagesJson = json, ContentHash = PipelineStagesJson.ContentHash(json),
            ChangeNote = note, CreatedAt = now
        };
    }

    private static string ValidateName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 200)
            throw new ValidationException("name", "Name must be 1 to 200 characters.", "pipeline_definition_name_invalid");
        return value;
    }

    private static void ValidateDescription(string? description)
    {
        if (description is null || description.Length > 2000)
            throw new ValidationException("description", "Description must be at most 2000 characters.", "pipeline_definition_description_invalid");
    }

    public static void ValidateStages(IReadOnlyList<PipelineStageSpec>? stages)
    {
        if (stages is null || stages.Count == 0)
            throw new ValidationException("stages", "At least one stage is required.", "pipeline_stages_empty");
        var roles = stages.Select(s => s.Role).ToHashSet();
        if (roles.Count != stages.Count)
            throw new ValidationException("stages", "Each role may appear once.", "pipeline_stage_role_duplicate");
        foreach (var stage in stages)
        {
            if (!AgentTaskRoles.IsStage(stage.Role))
                throw new ValidationException("stages", $"{stage.Role} is not a pipeline stage.", "pipeline_stage_role_not_stage");
            if (!stage.BundleKey.StartsWith("stage-", StringComparison.Ordinal))
                throw new ValidationException("stages", $"{stage.BundleKey} is not a stage bundle.", "pipeline_stage_bundle_not_stage");
            if (!InstructionBundles.IsStageBundleKey(stage.BundleKey))
                throw new ValidationException("stages", $"{stage.BundleKey} is unknown.", "pipeline_stage_bundle_unknown");
            foreach (var raw in stage.AllowedNext)
            {
                var parsed = PipelineHandoff.TryParse($"--- next stage ---\nnext: {raw}\n");
                if (parsed.Kind is not { } kind)
                    throw new ValidationException("stages", $"Unknown next token '{raw}'.", "pipeline_stage_next_unknown");
                if (PipelineHandoff.TryToStageRole(kind, out var nextRole) && !roles.Contains(nextRole))
                    throw new ValidationException("stages", $"{raw} is not in this revision.", "pipeline_stage_next_not_in_revision");
            }
        }
    }

    private static void RefuseBuiltIn(PipelineDefinition definition)
    {
        if (definition.Source == PipelineDefinitionSource.BuiltIn)
            throw new ConflictException("The built-in pipeline is code-owned; clone it to edit.", "pipeline_definition_builtin");
    }

    private static PipelineDefinitionDto ToDto(PipelineDefinition definition)
    {
        PipelineDefinitionRevisionDto? revision = null;
        if (definition.ActiveRevision is { } active)
            revision = new PipelineDefinitionRevisionDto(active.Id, active.RevisionNumber,
                active.ContentHash, PipelineStagesJson.Parse(active.StagesJson), active.ChangeNote, active.CreatedAt);
        return new PipelineDefinitionDto(definition.Id, definition.Name, definition.Description,
            definition.Source, definition.ActiveRevisionId, revision, definition.ArchivedAt,
            definition.ArchivedReason, definition.ArchivedBy, definition.CreatedAt, definition.UpdatedAt);
    }
}

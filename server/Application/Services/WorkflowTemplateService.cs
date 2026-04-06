using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Services;

public class WorkflowTemplateService
{
    private readonly AppDbContext _db;

    public WorkflowTemplateService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<WorkflowTemplateDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var templates = await _db.WorkflowTemplates
            .Include(t => t.TemplateGroup)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return templates.Select(ToDto).ToList();
    }

    public async Task<WorkflowTemplateDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _db.WorkflowTemplates
            .Include(t => t.TemplateGroup)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowTemplate), id);

        return ToDto(template);
    }

    public async Task<WorkflowTemplateDto> CreateAsync(
        CreateWorkflowTemplateRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request.Name, request.YamlDefinition);

        var template = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            YamlDefinition = request.YamlDefinition,
            IsBuiltIn = false,
            TemplateGroupId = request.TemplateGroupId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.WorkflowTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        // Reload with group nav prop so the DTO can include group name
        await _db.Entry(template).Reference(t => t.TemplateGroup).LoadAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task<WorkflowTemplateDto> UpdateAsync(
        Guid id, UpdateWorkflowTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await _db.WorkflowTemplates
            .Include(t => t.TemplateGroup)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowTemplate), id);

        ValidateRequest(request.Name, request.YamlDefinition);

        template.Name = request.Name;
        template.Description = request.Description;
        template.YamlDefinition = request.YamlDefinition;
        template.TemplateGroupId = request.TemplateGroupId;
        template.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Refresh nav prop if group changed
        await _db.Entry(template).Reference(t => t.TemplateGroup).LoadAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _db.WorkflowTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(WorkflowTemplate), id);

        if (template.IsBuiltIn)
        {
            throw new ConflictException("Built-in templates cannot be deleted.");
        }

        _db.WorkflowTemplates.Remove(template);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateRequest(string name, string yamlDefinition)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Name is required."];
        }

        if (string.IsNullOrWhiteSpace(yamlDefinition))
        {
            errors["yamlDefinition"] = ["YAML definition is required."];
        }
        else
        {
            var yamlErrors = ValidateYamlStructure(yamlDefinition);
            if (yamlErrors.Count > 0)
            {
                errors["yamlDefinition"] = yamlErrors.ToArray();
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    internal static List<string> ValidateYamlStructure(string yaml)
    {
        var errors = new List<string>();

        try
        {
            var yamlStream = new YamlStream();
            using var reader = new StringReader(yaml);
            yamlStream.Load(reader);

            if (yamlStream.Documents.Count == 0)
            {
                errors.Add("YAML document is empty.");
                return errors;
            }

            var root = yamlStream.Documents[0].RootNode;
            if (root is not YamlMappingNode rootMapping)
            {
                errors.Add("YAML root must be a mapping.");
                return errors;
            }

            // Validate stages array exists
            var stagesKey = new YamlScalarNode("stages");
            if (!rootMapping.Children.ContainsKey(stagesKey))
            {
                errors.Add("YAML must contain a 'stages' array.");
                return errors;
            }

            if (rootMapping.Children[stagesKey] is not YamlSequenceNode stagesSequence)
            {
                errors.Add("'stages' must be an array.");
                return errors;
            }

            if (stagesSequence.Children.Count == 0)
            {
                errors.Add("'stages' array must contain at least one stage.");
                return errors;
            }

            // Validate each stage
            for (var i = 0; i < stagesSequence.Children.Count; i++)
            {
                if (stagesSequence.Children[i] is not YamlMappingNode stage)
                {
                    errors.Add($"Stage at index {i} must be a mapping.");
                    continue;
                }

                var nameKey = new YamlScalarNode("name");
                if (!stage.Children.ContainsKey(nameKey))
                {
                    errors.Add($"Stage at index {i} must have a 'name' field.");
                }

                var executorTypeKey = new YamlScalarNode("executorType");
                if (!stage.Children.ContainsKey(executorTypeKey))
                {
                    errors.Add($"Stage at index {i} must have an 'executorType' field.");
                }
            }
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            errors.Add($"Invalid YAML syntax: {ex.Message}");
        }

        return errors;
    }

    private static WorkflowTemplateDto ToDto(WorkflowTemplate entity)
    {
        // Parse selectableStages from YAML without throwing — default to false on any parse error
        var selectableStages = false;
        try
        {
            var yamlStream = new YamlStream();
            using var reader = new StringReader(entity.YamlDefinition);
            yamlStream.Load(reader);
            if (yamlStream.Documents.Count > 0
                && yamlStream.Documents[0].RootNode is YamlMappingNode root)
            {
                var key = new YamlScalarNode("selectableStages");
                if (root.Children.TryGetValue(key, out var node) && node is YamlScalarNode scalar)
                {
                    selectableStages = string.Equals(
                        scalar.Value, "true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // Ignore — return false
        }

        return new WorkflowTemplateDto(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.YamlDefinition,
            entity.IsBuiltIn,
            entity.TemplateGroupId,
            entity.TemplateGroup?.Name,
            selectableStages,
            entity.CreatedAt,
            entity.UpdatedAt);
    }
}

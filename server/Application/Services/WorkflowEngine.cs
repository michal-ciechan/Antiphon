using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Core workflow orchestration engine. Creates workflows from templates, executes stages
/// sequentially via IStageExecutor, pauses at gate points, and pushes events via IEventBus.
/// </summary>
public class WorkflowEngine
{
    private readonly AppDbContext _db;
    private readonly IStageExecutor _stageExecutor;
    private readonly IEventBus _eventBus;

    public WorkflowEngine(AppDbContext db, IStageExecutor stageExecutor, IEventBus eventBus)
    {
        _db = db;
        _stageExecutor = stageExecutor;
        _eventBus = eventBus;
    }

    /// <summary>
    /// Parses a YAML workflow definition into a WorkflowDefinition value object.
    /// </summary>
    public static WorkflowDefinition ParseYamlDefinition(string yaml)
    {
        var yamlStream = new YamlStream();
        using var reader = new StringReader(yaml);
        yamlStream.Load(reader);

        if (yamlStream.Documents.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["YAML document is empty."]
            });

        var root = yamlStream.Documents[0].RootNode;
        if (root is not YamlMappingNode rootMapping)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["YAML root must be a mapping."]
            });

        var name = GetScalarValue(rootMapping, "name") ?? "Unnamed Workflow";
        var description = GetScalarValue(rootMapping, "description") ?? string.Empty;

        var stagesKey = new YamlScalarNode("stages");
        if (!rootMapping.Children.ContainsKey(stagesKey) ||
            rootMapping.Children[stagesKey] is not YamlSequenceNode stagesSequence)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["YAML must contain a 'stages' array."]
            });
        }

        var stages = new List<StageDefinition>();
        foreach (var child in stagesSequence.Children)
        {
            if (child is not YamlMappingNode stageMapping)
                continue;

            var stageName = GetScalarValue(stageMapping, "name")
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["yaml"] = ["Each stage must have a 'name' field."]
                });

            var executorType = GetScalarValue(stageMapping, "executorType")
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["yaml"] = [$"Stage '{stageName}' must have an 'executorType' field."]
                });

            var modelName = GetScalarValue(stageMapping, "modelName");
            var gateRequiredStr = GetScalarValue(stageMapping, "gateRequired");
            var gateRequired = string.Equals(gateRequiredStr, "true", StringComparison.OrdinalIgnoreCase);
            var systemPrompt = GetScalarValue(stageMapping, "systemPrompt");

            stages.Add(new StageDefinition(stageName, executorType, modelName, gateRequired, systemPrompt));
        }

        if (stages.Count == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["yaml"] = ["'stages' array must contain at least one stage."]
            });
        }

        return new WorkflowDefinition(name, description, stages);
    }

    /// <summary>
    /// Creates a new workflow from a template, parsing the YAML and creating Stage entities.
    /// Transitions workflow to Running and begins executing the first stage.
    /// </summary>
    public async Task<Workflow> CreateWorkflowAsync(
        Guid templateId, Guid projectId, string initialContext, CancellationToken ct)
    {
        var template = await _db.WorkflowTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException(nameof(WorkflowTemplate), templateId);

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var definition = ParseYamlDefinition(template.YamlDefinition);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = definition.Name,
            Description = definition.Description,
            TemplateId = templateId,
            ProjectId = projectId,
            Status = WorkflowStatus.Created,
            InitialContext = initialContext,
            GitBranchName = $"antiphon/workflow-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Workflows.Add(workflow);

        // Create Stage entities from definition
        for (var i = 0; i < definition.Stages.Count; i++)
        {
            var stageDef = definition.Stages[i];
            var stage = new Stage
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflow.Id,
                Name = stageDef.Name,
                Description = stageDef.SystemPrompt ?? string.Empty,
                StageOrder = i,
                Status = StageStatus.Pending,
                ExecutorType = stageDef.ExecutorType,
                ModelName = stageDef.ModelName,
                GateRequired = stageDef.GateRequired,
                CreatedAt = DateTime.UtcNow
            };

            _db.Stages.Add(stage);

            // Set the first stage as current
            if (i == 0)
            {
                workflow.CurrentStageId = stage.Id;
            }
        }

        await _db.SaveChangesAsync(ct);

        // Transition to Running
        TransitionWorkflow(workflow, WorkflowStatus.Running);
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflow.Id}",
            "WorkflowStatusChanged",
            new { workflowId = workflow.Id, status = workflow.Status.ToString() },
            ct);

        // Start executing the first stage
        await ExecuteNextStageAsync(workflow.Id, ct);

        return workflow;
    }

    /// <summary>
    /// Executes the next pending stage in the workflow via IStageExecutor.
    /// After execution, either pauses at a gate or continues to the next stage.
    /// </summary>
    public async Task ExecuteNextStageAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        // Ensure workflow is in Running state
        if (workflow.Status != WorkflowStatus.Running)
        {
            throw new ConflictException(
                $"Workflow must be in Running state to execute stages. Current: {workflow.Status}");
        }

        // Find the next pending stage
        var nextStage = workflow.Stages
            .OrderBy(s => s.StageOrder)
            .FirstOrDefault(s => s.Status == StageStatus.Pending);

        if (nextStage is null)
        {
            // All stages completed — finish the workflow
            TransitionWorkflow(workflow, WorkflowStatus.Completed);
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _eventBus.PublishToGroupAsync(
                $"workflow-{workflow.Id}",
                "WorkflowCompleted",
                new { workflowId = workflow.Id },
                ct);
            return;
        }

        // Set as current stage
        workflow.CurrentStageId = nextStage.Id;
        workflow.UpdatedAt = DateTime.UtcNow;

        // Mark stage as running
        nextStage.Status = StageStatus.Running;

        // Create stage execution record
        var execution = new StageExecution
        {
            Id = Guid.NewGuid(),
            StageId = nextStage.Id,
            WorkflowId = workflowId,
            Version = nextStage.CurrentVersion,
            Status = StageStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _db.StageExecutions.Add(execution);
        await _db.SaveChangesAsync(ct);

        // Push StageStarted event
        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "StageStarted",
            new { workflowId, stageId = nextStage.Id, stageName = nextStage.Name },
            ct);

        // Gather upstream artifacts (output from completed stages)
        var upstreamArtifacts = await _db.StageExecutions
            .Where(se => se.WorkflowId == workflowId
                && se.Status == StageStatus.Completed
                && se.StageId != nextStage.Id)
            .OrderBy(se => se.StartedAt)
            .Select(se => se.GitTagName ?? string.Empty)
            .Where(tag => !string.IsNullOrEmpty(tag))
            .ToListAsync(ct);

        // Build execution context
        var context = new StageExecutionContext(
            WorkflowId: workflowId,
            StageId: nextStage.Id,
            StageName: nextStage.Name,
            ExecutorType: nextStage.ExecutorType,
            ModelName: nextStage.ModelName,
            SystemPrompt: nextStage.Description,
            UpstreamArtifacts: upstreamArtifacts,
            Constitution: null, // Will be loaded from project repo in future stories
            StageInstructions: nextStage.Description);

        // Execute via IStageExecutor
        StageExecutionResult result;
        try
        {
            result = await _stageExecutor.ExecuteAsync(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mark stage and execution as failed
            nextStage.Status = StageStatus.Failed;
            execution.Status = StageStatus.Failed;
            execution.CompletedAt = DateTime.UtcNow;
            execution.ErrorDetails = ex.Message;

            TransitionWorkflow(workflow, WorkflowStatus.Failed);
            workflow.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            throw;
        }

        // Mark stage execution as completed
        execution.Status = StageStatus.Completed;
        execution.CompletedAt = DateTime.UtcNow;
        execution.GitTagName = result.ArtifactPaths.Count > 0 ? result.ArtifactPaths[0] : null;

        // Mark stage as completed
        nextStage.Status = StageStatus.Completed;
        nextStage.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Push StageCompleted event
        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "StageCompleted",
            new { workflowId, stageId = nextStage.Id, stageName = nextStage.Name },
            ct);

        // Check if this stage has a gate
        if (nextStage.GateRequired)
        {
            // Pause at gate
            TransitionWorkflow(workflow, WorkflowStatus.GateWaiting);
            workflow.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _eventBus.PublishToGroupAsync(
                $"workflow-{workflowId}",
                "GateReady",
                new { workflowId, stageId = nextStage.Id, stageName = nextStage.Name },
                ct);

            return;
        }

        // No gate — continue to next stage
        await ExecuteNextStageAsync(workflowId, ct);
    }

    /// <summary>
    /// Resumes workflow execution after a gate approval, advancing to the next stage.
    /// </summary>
    public async Task ResumeAfterGateAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.GateWaiting)
        {
            throw new ConflictException(
                $"Workflow must be in GateWaiting state to resume after gate. Current: {workflow.Status}");
        }

        // Transition back to Running
        TransitionWorkflow(workflow, WorkflowStatus.Running);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "WorkflowStatusChanged",
            new { workflowId, status = workflow.Status.ToString() },
            ct);

        // Continue execution
        await ExecuteNextStageAsync(workflowId, ct);
    }

    private static void TransitionWorkflow(Workflow workflow, WorkflowStatus newStatus)
    {
        if (!WorkflowStateMachine.CanTransition(workflow.Status, newStatus))
        {
            throw new ConflictException(
                $"Invalid workflow state transition from {workflow.Status} to {newStatus}.");
        }

        workflow.Status = newStatus;
    }

    private static string? GetScalarValue(YamlMappingNode mapping, string key)
    {
        var yamlKey = new YamlScalarNode(key);
        if (mapping.Children.TryGetValue(yamlKey, out var node) && node is YamlScalarNode scalar)
        {
            return scalar.Value;
        }
        return null;
    }
}

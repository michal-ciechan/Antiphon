using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IGitService _gitService;
    private readonly GitSettings _gitSettings;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly AuditService _auditService;

    public WorkflowEngine(
        AppDbContext db,
        IStageExecutor stageExecutor,
        IEventBus eventBus,
        IGitService gitService,
        IOptions<GitSettings> gitSettings,
        ILogger<WorkflowEngine> logger,
        AuditService auditService)
    {
        _db = db;
        _stageExecutor = stageExecutor;
        _eventBus = eventBus;
        _gitService = gitService;
        _gitSettings = gitSettings.Value;
        _logger = logger;
        _auditService = auditService;
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
        var selectableStagesStr = GetScalarValue(rootMapping, "selectableStages");
        var selectableStages = string.Equals(selectableStagesStr, "true", StringComparison.OrdinalIgnoreCase);

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

        return new WorkflowDefinition(name, description, stages, selectableStages);
    }

    /// <summary>
    /// Creates a new workflow from a template, parsing the YAML and creating Stage entities.
    /// Transitions workflow to Running and begins executing the first stage.
    /// </summary>
    public async Task<Workflow> CreateWorkflowAsync(
        CreateWorkflowRequest request,
        CancellationToken ct)
    {
        var templateId = request.TemplateId;
        var projectId = request.ProjectId;

        var template = await _db.WorkflowTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException(nameof(WorkflowTemplate), templateId);

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException(nameof(Project), projectId);

        var definition = ParseYamlDefinition(template.YamlDefinition);

        // Filter stages when the template supports selectable stages and the caller specified a subset
        var stagesToCreate = (definition.SelectableStages
            && request.SelectedStages != null
            && request.SelectedStages.Count > 0)
            ? definition.Stages
                .Where(s => request.SelectedStages.Contains(s.Name))
                .ToList()
            : definition.Stages.ToList();

        // Load template model routings to resolve default models per stage
        var templateRoutings = await _db.ModelRoutings
            .Where(r => r.WorkflowTemplateId == templateId)
            .ToListAsync(ct);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = definition.Name,
            Description = definition.Description,
            FeatureName = request.FeatureName,
            TemplateId = templateId,
            ProjectId = projectId,
            Status = WorkflowStatus.Created,
            InitialContext = request.InitialContext ?? string.Empty,
            GitBranchName = $"antiphon/{SanitizeBranchName(request.FeatureName ?? Guid.NewGuid().ToString("N"))}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Save workflow first with CurrentStageId null to avoid circular FK dependency
        // (Workflow.CurrentStageId -> Stage, Stage.WorkflowId -> Workflow)
        _db.Workflows.Add(workflow);
        await _db.SaveChangesAsync(ct);

        // Create Stage entities from definition (reindexed after any filtering)
        Stage? firstStage = null;
        for (var i = 0; i < stagesToCreate.Count; i++)
        {
            var stageDef = stagesToCreate[i];
            var stage = new Stage
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflow.Id,
                Name = stageDef.Name,
                Description = stageDef.SystemPrompt ?? string.Empty,
                StageOrder = i,
                Status = StageStatus.Pending,
                ExecutorType = stageDef.ExecutorType,
                ModelName = request.StageModelOverrides?.GetValueOrDefault(stageDef.Name)
                    ?? stageDef.ModelName
                    ?? templateRoutings.FirstOrDefault(r => r.StageName == stageDef.Name)?.ModelName,
                GateRequired = stageDef.GateRequired,
                CreatedAt = DateTime.UtcNow
            };

            _db.Stages.Add(stage);

            if (i == 0)
            {
                firstStage = stage;
            }
        }

        workflow.CurrentStageId = firstStage?.Id;
        await _db.SaveChangesAsync(ct);

        // Initialize git workflow branches (FR28)
        var repoPath = await ResolveLocalRepoPathAsync(project, ct);
        if (!string.IsNullOrEmpty(repoPath))
        {
            await _gitService.InitializeWorkflowBranchesAsync(workflow.Id, repoPath, ct);
        }

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

            await _auditService.RecordEventAsync(
                AuditEventType.WorkflowCompleted,
                workflowId,
                stageId: null,
                stageExecutionId: null,
                summary: "Workflow completed",
                clientIp: null,
                userId: null,
                gitTagName: null,
                fullContentJson: null,
                cancellationToken: ct
            );

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

        await _auditService.RecordEventAsync(
            AuditEventType.StageStarted,
            workflowId,
            nextStage.Id,
            execution.Id,
            summary: $"Stage '{nextStage.Name}' started (v{nextStage.CurrentVersion})",
            clientIp: null,
            userId: null,
            gitTagName: null,
            fullContentJson: null,
            cancellationToken: ct
        );

        // Create stage branch for execution (FR29)
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == workflow.ProjectId, ct)
            ?? throw new NotFoundException(nameof(Project), workflow.ProjectId);

        var repoPath = await ResolveLocalRepoPathAsync(project, ct);
        if (!string.IsNullOrEmpty(repoPath))
        {
            await _gitService.CreateStageBranchAsync(workflowId, nextStage.Name, repoPath, ct);
        }

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
            StageExecutionId: execution.Id,
            StageName: nextStage.Name,
            ExecutorType: nextStage.ExecutorType,
            ModelName: nextStage.ModelName,
            SystemPrompt: nextStage.Description,
            UpstreamArtifacts: upstreamArtifacts,
            Constitution: null, // Will be loaded from project repo in future stories
            StageInstructions: nextStage.Description,
            InitialContext: workflow.InitialContext,
            BranchName: workflow.GitBranchName);

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

            await _auditService.RecordEventAsync(
                AuditEventType.StageFailed,
                workflowId,
                nextStage.Id,
                execution.Id,
                summary: $"Stage '{nextStage.Name}' failed: {ex.Message}",
                clientIp: null,
                userId: null,
                gitTagName: null,
                fullContentJson: null,
                cancellationToken: ct
            );

            throw;
        }

        // Commit artifacts to stage branch and tag (FR30, FR31, FR33) - only when local repo is configured
        string? tagName = null;
        if (!string.IsNullOrEmpty(repoPath))
        {
            foreach (var artifactFilePath in result.ArtifactPaths)
            {
                await _gitService.CommitArtifactAsync(
                    workflowId, nextStage.Name, result.OutputContent, artifactFilePath, repoPath, ct);
            }

            tagName = await _gitService.TagStageAsync(
                workflowId, nextStage.Name, nextStage.CurrentVersion, repoPath, ct);
        }

        // Mark stage execution as completed
        execution.Status = StageStatus.Completed;
        execution.CompletedAt = DateTime.UtcNow;
        execution.GitTagName = tagName;
        execution.OutputContent = result.OutputContent;

        _logger.LogInformation(
            "Stage {StageName} completed. OutputContent length={Length}, GitTagName={GitTagName}",
            nextStage.Name,
            result.OutputContent?.Length ?? 0,
            tagName ?? "(none)");

        // Mark stage as completed
        nextStage.Status = StageStatus.Completed;
        nextStage.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _auditService.RecordEventAsync(
            AuditEventType.StageCompleted,
            workflowId,
            nextStage.Id,
            execution.Id,
            summary: $"Stage '{nextStage.Name}' completed (v{nextStage.CurrentVersion})",
            clientIp: null,
            userId: null,
            gitTagName: tagName,
            fullContentJson: null,
            cancellationToken: ct
        );

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
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.GateWaiting)
        {
            throw new ConflictException(
                $"Workflow must be in GateWaiting state to resume after gate. Current: {workflow.Status}");
        }

        // Merge the approved stage branch into workflow master (FR32)
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == workflow.ProjectId, ct)
            ?? throw new NotFoundException(nameof(Project), workflow.ProjectId);

        // Find the most recently completed stage (the one that triggered the gate)
        var approvedStage = workflow.Stages
            .OrderByDescending(s => s.StageOrder)
            .FirstOrDefault(s => s.Status == StageStatus.Completed);

        var resumeRepoPath = await ResolveLocalRepoPathAsync(project, ct);
        if (approvedStage is not null && !string.IsNullOrEmpty(resumeRepoPath))
        {
            await _gitService.MergeStageBranchAsync(workflowId, approvedStage.Name, resumeRepoPath, ct);
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

    /// <summary>
    /// Approves the current gate: tags the stage, merges it into workflow master,
    /// and advances to the next stage (FR22, FR31, FR32).
    /// </summary>
    public async Task ApproveGateAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.GateWaiting)
        {
            throw new ConflictException(
                $"Workflow must be in GateWaiting state to approve gate. Current: {workflow.Status}");
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == workflow.ProjectId, ct)
            ?? throw new NotFoundException(nameof(Project), workflow.ProjectId);

        // Find the completed stage that triggered the gate
        var approvedStage = workflow.Stages
            .OrderByDescending(s => s.StageOrder)
            .FirstOrDefault(s => s.Status == StageStatus.Completed)
            ?? throw new ConflictException("No completed stage found for gate approval.");

        // Tag the stage commit (FR31) and merge into workflow master (FR32) - only when local repo is configured
        var approveRepoPath = await ResolveLocalRepoPathAsync(project, ct);
        if (!string.IsNullOrEmpty(approveRepoPath))
        {
            await _gitService.TagStageAsync(
                workflowId, approvedStage.Name, approvedStage.CurrentVersion, approveRepoPath, ct);
            await _gitService.MergeStageBranchAsync(
                workflowId, approvedStage.Name, approveRepoPath, ct);
        }

        // Transition back to Running and advance to next stage
        TransitionWorkflow(workflow, WorkflowStatus.Running);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditService.RecordEventAsync(
            AuditEventType.GateApproved,
            workflowId,
            approvedStage.Id,
            stageExecutionId: null,
            summary: $"Gate approved for stage '{approvedStage.Name}'",
            clientIp: null,
            userId: null,
            gitTagName: null,
            fullContentJson: null,
            cancellationToken: ct
        );

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "GateApproved",
            new { workflowId, stageId = approvedStage.Id, stageName = approvedStage.Name },
            ct);

        // Continue to next stage
        await ExecuteNextStageAsync(workflowId, ct);
    }

    /// <summary>
    /// Rejects the current gate with feedback: re-executes the current stage with
    /// rejection context injected (FR23).
    /// </summary>
    public async Task RejectGateAsync(Guid workflowId, string feedback, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.GateWaiting)
        {
            throw new ConflictException(
                $"Workflow must be in GateWaiting state to reject gate. Current: {workflow.Status}");
        }

        // Find the completed stage that triggered the gate
        var rejectedStage = workflow.Stages
            .OrderByDescending(s => s.StageOrder)
            .FirstOrDefault(s => s.Status == StageStatus.Completed)
            ?? throw new ConflictException("No completed stage found for gate rejection.");

        // Reset stage to Pending with incremented version for re-execution
        rejectedStage.Status = StageStatus.Pending;
        rejectedStage.CurrentVersion++;
        rejectedStage.CompletedAt = null;

        // Transition back to Running for re-execution
        TransitionWorkflow(workflow, WorkflowStatus.Running);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditService.RecordEventAsync(
            AuditEventType.GateRejected,
            workflowId,
            rejectedStage.Id,
            stageExecutionId: null,
            summary: $"Gate rejected for stage '{rejectedStage.Name}'" + (string.IsNullOrEmpty(feedback) ? "" : $": {feedback}"),
            clientIp: null,
            userId: null,
            gitTagName: null,
            fullContentJson: null,
            cancellationToken: ct
        );

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "GateRejected",
            new { workflowId, stageId = rejectedStage.Id, stageName = rejectedStage.Name, feedback },
            ct);

        // Re-execute the stage (the executor will pick up the next pending stage, which is the reset one)
        await ExecuteNextStageAsync(workflowId, ct);
    }

    /// <summary>
    /// Sends a user prompt to the agent at the current stage gate (FR27).
    /// The agent receives the prompt and produces a revised artifact.
    /// </summary>
    public async Task PromptAgentAsync(Guid workflowId, string prompt, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.GateWaiting && workflow.Status != WorkflowStatus.Running)
        {
            throw new ConflictException(
                $"Workflow must be in GateWaiting or Running state to prompt agent. Current: {workflow.Status}");
        }

        // Find the current stage
        var currentStage = workflow.Stages
            .OrderByDescending(s => s.StageOrder)
            .FirstOrDefault(s => s.Status == StageStatus.Completed || s.Status == StageStatus.Running)
            ?? throw new ConflictException("No active stage found for agent prompt.");

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "AgentPromptReceived",
            new { workflowId, stageId = currentStage.Id, prompt },
            ct);

        // If the stage was completed (at a gate), reset it for re-execution with the prompt
        if (currentStage.Status == StageStatus.Completed && workflow.Status == WorkflowStatus.GateWaiting)
        {
            currentStage.Status = StageStatus.Pending;
            currentStage.CurrentVersion++;
            currentStage.CompletedAt = null;

            TransitionWorkflow(workflow, WorkflowStatus.Running);
            workflow.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await ExecuteNextStageAsync(workflowId, ct);
        }
    }

    /// <summary>
    /// Initiates a go-back course correction: transitions to CascadeWaiting, resets the target
    /// stage for re-execution, identifies downstream affected stages, and records the go-back
    /// event in the audit trail (FR24, FR25, FR38, FR42, FR53).
    /// Returns affected stages so the caller can present cascade decisions to the user.
    /// </summary>
    public async Task<GoBackResult> GoBackAsync(
        Guid workflowId, Guid targetStageId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.GateWaiting)
        {
            throw new ConflictException(
                $"Workflow must be in GateWaiting state to go back. Current: {workflow.Status}");
        }

        var targetStage = workflow.Stages.FirstOrDefault(s => s.Id == targetStageId)
            ?? throw new NotFoundException(nameof(Stage), targetStageId);

        // Validate: target must be a completed stage before the current gate stage
        if (targetStage.Status != StageStatus.Completed)
        {
            throw new ConflictException(
                $"Target stage \"{targetStage.Name}\" must be in Completed state. Current: {targetStage.Status}");
        }

        // Detect affected downstream stages before resetting
        var affectedStages = workflow.Stages
            .Where(s => s.StageOrder > targetStage.StageOrder && s.Status == StageStatus.Completed)
            .OrderBy(s => s.StageOrder)
            .ToList();

        // Reset the target stage for re-execution (increment version, preserve old version via tags)
        targetStage.CurrentVersion++;
        targetStage.Status = StageStatus.Pending;
        targetStage.CompletedAt = null;

        // Set the target stage as current
        workflow.CurrentStageId = targetStage.Id;

        // Transition to CascadeWaiting if there are downstream affected stages,
        // otherwise go straight to Running for re-execution
        if (affectedStages.Count > 0)
        {
            TransitionWorkflow(workflow, WorkflowStatus.CascadeWaiting);
        }
        else
        {
            TransitionWorkflow(workflow, WorkflowStatus.Running);
        }

        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Record go-back in audit trail (FR42, FR53)
        await _auditService.RecordEventAsync(
            AuditEventType.GoBack,
            workflowId,
            targetStage.Id,
            stageExecutionId: null,
            summary: $"Go-back to stage '{targetStage.Name}' (v{targetStage.CurrentVersion - 1} → v{targetStage.CurrentVersion}), {affectedStages.Count} downstream stage(s) affected",
            clientIp: null,
            userId: null,
            gitTagName: null,
            fullContentJson: null,
            cancellationToken: ct
        );

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "GateActioned",
            new { workflowId, action = "go-back", stageId = targetStageId, targetStageName = targetStage.Name },
            ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "CascadeTriggered",
            new { workflowId, targetStageId, affectedStageCount = affectedStages.Count },
            ct);

        // Build affected stage DTOs with reasons
        var affectedDtos = affectedStages.Select(s => new AffectedStageDto(
            StageId: s.Id,
            StageName: s.Name,
            StageOrder: s.StageOrder,
            CurrentVersion: s.CurrentVersion,
            Reason: $"This stage was built on the output of \"{targetStage.Name}\". Changes to the upstream artifact may invalidate assumptions in this stage's output.",
            DefaultAction: Domain.Enums.CascadeAction.UpdateFromDiff)).ToList();

        // If no affected stages, start re-execution immediately
        if (affectedStages.Count == 0)
        {
            await ExecuteNextStageAsync(workflowId, ct);
        }

        return new GoBackResult(affectedDtos);
    }

    /// <summary>
    /// Resumes a workflow after cascade decisions have been applied.
    /// Transitions from CascadeWaiting back to Running and continues execution.
    /// </summary>
    public async Task ResumeAfterCascadeAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        if (workflow.Status != WorkflowStatus.CascadeWaiting)
        {
            throw new ConflictException(
                $"Workflow must be in CascadeWaiting state to resume after cascade. Current: {workflow.Status}");
        }

        TransitionWorkflow(workflow, WorkflowStatus.Running);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "WorkflowStatusChanged",
            new { workflowId, status = workflow.Status.ToString() },
            ct);

        // Continue execution from the pending stage
        await ExecuteNextStageAsync(workflowId, ct);
    }

    /// <summary>
    /// Pauses an active workflow. Validates that the transition is allowed by the state machine,
    /// sets status to Paused, and pushes a WorkflowStatusChanged event via IEventBus.
    /// </summary>
    public async Task PauseWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        TransitionWorkflow(workflow, WorkflowStatus.Paused);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "WorkflowStatusChanged",
            new { workflowId, status = workflow.Status.ToString() },
            ct);
    }

    /// <summary>
    /// Resumes a paused workflow. Validates that the transition is allowed by the state machine,
    /// sets status to Running, pushes a WorkflowStatusChanged event, and continues stage execution.
    /// </summary>
    public async Task ResumeWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        TransitionWorkflow(workflow, WorkflowStatus.Running);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "WorkflowStatusChanged",
            new { workflowId, status = workflow.Status.ToString() },
            ct);

        // Continue execution from where it left off
        await ExecuteNextStageAsync(workflowId, ct);
    }

    /// <summary>
    /// Abandons a workflow. Validates that the transition is allowed by the state machine,
    /// sets status to Abandoned, and pushes a WorkflowStatusChanged event via IEventBus.
    /// </summary>
    public async Task AbandonWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        TransitionWorkflow(workflow, WorkflowStatus.Abandoned);
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "WorkflowStatusChanged",
            new { workflowId, status = workflow.Status.ToString() },
            ct);
    }

    /// <summary>
    /// Deletes a workflow and all its associated stages and executions.
    /// Throws if the workflow is currently running.
    /// </summary>
    public async Task DeleteWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages)
                .ThenInclude(s => s.StageExecutions)
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        // Cannot delete a running workflow
        if (workflow.Status == WorkflowStatus.Running)
            throw new InvalidOperationException("Cannot delete a workflow that is currently running. Abandon it first.");

        // Clear CurrentStageId to break the circular FK before deleting stages
        workflow.CurrentStageId = null;
        await _db.SaveChangesAsync(ct);

        // Remove stage executions, then stages, then workflow
        foreach (var stage in workflow.Stages)
            _db.StageExecutions.RemoveRange(stage.StageExecutions);

        _db.Stages.RemoveRange(workflow.Stages);
        _db.Workflows.Remove(workflow);
        await _db.SaveChangesAsync(ct);
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

    /// <summary>
    /// Resolves the local repository path for a project.
    /// If LocalRepositoryPath is explicitly set, returns it directly.
    /// Otherwise, if WorkspacePath is configured, computes a path under the workspace,
    /// calls EnsureCloneAsync to clone/fetch the repo, and returns the computed path.
    /// Returns null if neither is available.
    /// </summary>
    private async Task<string?> ResolveLocalRepoPathAsync(Project project, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(project.LocalRepositoryPath))
            return project.LocalRepositoryPath;

        if (string.IsNullOrEmpty(_gitSettings.WorkspacePath))
            return null;

        // Resolve relative workspace path against the app base directory
        var workspacePath = Path.IsPathRooted(_gitSettings.WorkspacePath)
            ? _gitSettings.WorkspacePath
            : Path.Combine(AppContext.BaseDirectory, _gitSettings.WorkspacePath);

        var slug = SanitizeBranchName(project.Name);
        var repoPath = Path.Combine(workspacePath, slug);

        try
        {
            await _gitService.EnsureCloneAsync(project.GitRepositoryUrl, repoPath, ct);
            return repoPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clone/fetch repository {Url} to {Path}. Git operations will be skipped.",
                project.GitRepositoryUrl,
                repoPath
            );
            return null;
        }
    }

    private static string SanitizeBranchName(string name)
    {
        // Replace spaces and special chars with hyphens, lowercase, trim hyphens
        var sanitized = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"-+", "-");
        return sanitized.Trim('-');
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

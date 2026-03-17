using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Detects downstream stages affected by a go-back course correction,
/// computes reasons for each affected stage, and executes cascade decisions
/// (UpdateFromDiff / Regenerate / KeepAsIs) per FR25, FR26, FR34, FR38, FR40, FR41, FR42.
/// </summary>
public class CascadeService
{
    private readonly AppDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IGitService _gitService;
    private readonly IStageExecutor _stageExecutor;

    public CascadeService(
        AppDbContext db,
        IEventBus eventBus,
        IGitService gitService,
        IStageExecutor stageExecutor)
    {
        _db = db;
        _eventBus = eventBus;
        _gitService = gitService;
        _stageExecutor = stageExecutor;
    }

    /// <summary>
    /// Identifies all stages downstream of <paramref name="targetStageId"/> that have
    /// already been completed and would be affected by a go-back (FR25, FR38).
    /// Returns a list of affected stages with reasons why they are impacted.
    /// </summary>
    public async Task<IReadOnlyList<AffectedStageDto>> DetectAffectedStagesAsync(
        Guid workflowId, Guid targetStageId, CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        var targetStage = workflow.Stages.FirstOrDefault(s => s.Id == targetStageId)
            ?? throw new NotFoundException(nameof(Stage), targetStageId);

        // All completed stages after the target stage are affected
        var affectedStages = workflow.Stages
            .Where(s => s.StageOrder > targetStage.StageOrder && s.Status == StageStatus.Completed)
            .OrderBy(s => s.StageOrder)
            .ToList();

        var result = new List<AffectedStageDto>();

        foreach (var stage in affectedStages)
        {
            var reason = $"This stage was built on the output of \"{targetStage.Name}\" (stage {targetStage.StageOrder + 1}). "
                + "Changes to the upstream artifact may invalidate assumptions in this stage's output.";

            result.Add(new AffectedStageDto(
                StageId: stage.Id,
                StageName: stage.Name,
                StageOrder: stage.StageOrder,
                CurrentVersion: stage.CurrentVersion,
                Reason: reason,
                DefaultAction: CascadeAction.UpdateFromDiff));
        }

        return result;
    }

    /// <summary>
    /// Executes the user's cascade decisions for each affected stage (FR34, FR40, FR41, FR42).
    /// For "UpdateFromDiff": computes git diff and has agent patch the downstream artifact.
    /// For "Regenerate": re-executes the stage from scratch.
    /// For "KeepAsIs": preserves the current version with no changes.
    /// All previous versions are preserved (FR41).
    /// </summary>
    public async Task ExecuteCascadeAsync(
        Guid workflowId,
        IReadOnlyList<CascadeDecisionDto> decisions,
        CancellationToken ct)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new NotFoundException(nameof(Workflow), workflowId);

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == workflow.ProjectId, ct)
            ?? throw new NotFoundException(nameof(Project), workflow.ProjectId);

        var repoPath = project.GitRepositoryUrl;

        // Find the target stage (the one the user went back to) — it's the most recently
        // completed stage that was reset to Pending during the go-back
        var goBackStage = workflow.Stages
            .OrderBy(s => s.StageOrder)
            .FirstOrDefault(s => s.Status == StageStatus.Pending || s.Status == StageStatus.Running);

        // Get the previous and current version tags for the go-back stage to compute diffs
        string? diffOutput = null;
        if (goBackStage is not null && goBackStage.CurrentVersion > 1)
        {
            var oldTag = $"antiphon/workflow-{workflowId}/{goBackStage.Name}-v{goBackStage.CurrentVersion - 1}";
            var newTag = $"antiphon/workflow-{workflowId}/{goBackStage.Name}-v{goBackStage.CurrentVersion}";
            var pathFilter = $"_antiphon/artifacts/workflow-{workflowId}/";

            try
            {
                diffOutput = await _gitService.GetDiffBetweenTagsAsync(
                    oldTag, newTag, repoPath, pathFilter, ct);
            }
            catch
            {
                // If tags don't exist yet, diff is unavailable — fall through to regenerate
                diffOutput = null;
            }
        }

        // Process each decision in stage order
        var orderedDecisions = decisions
            .OrderBy(d => workflow.Stages.FirstOrDefault(s => s.Id == d.StageId)?.StageOrder ?? int.MaxValue)
            .ToList();

        foreach (var decision in orderedDecisions)
        {
            var stage = workflow.Stages.FirstOrDefault(s => s.Id == decision.StageId)
                ?? throw new NotFoundException(nameof(Stage), decision.StageId);

            switch (decision.Action)
            {
                case CascadeAction.KeepAsIs:
                    // No changes — record in audit trail only
                    await RecordCascadeAuditAsync(workflowId, stage, decision.Action, ct);
                    break;

                case CascadeAction.Regenerate:
                    // Increment version, reset to Pending, re-execute from scratch
                    stage.CurrentVersion++;
                    stage.Status = StageStatus.Pending;
                    stage.CompletedAt = null;
                    await _db.SaveChangesAsync(ct);

                    await ExecuteStageAsync(workflow, stage, project, systemPromptOverride: null, ct);
                    await RecordCascadeAuditAsync(workflowId, stage, decision.Action, ct);
                    break;

                case CascadeAction.UpdateFromDiff:
                    // Increment version, use diff context to patch
                    stage.CurrentVersion++;
                    stage.Status = StageStatus.Pending;
                    stage.CompletedAt = null;
                    await _db.SaveChangesAsync(ct);

                    if (string.IsNullOrWhiteSpace(diffOutput))
                    {
                        // No diff available — fall back to regenerate
                        await ExecuteStageAsync(workflow, stage, project, systemPromptOverride: null, ct);
                    }
                    else
                    {
                        // Build a system prompt that includes the diff context
                        var patchPrompt = BuildDiffPatchPrompt(goBackStage!.Name, diffOutput);
                        await ExecuteStageAsync(workflow, stage, project, patchPrompt, ct);
                    }

                    await RecordCascadeAuditAsync(workflowId, stage, decision.Action, ct);
                    break;
            }
        }

        // After all cascade decisions are executed, transition workflow back to Running
        // and continue normal execution
        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "CascadeCompleted",
            new { workflowId },
            ct);
    }

    private async Task ExecuteStageAsync(
        Workflow workflow, Stage stage, Project project,
        string? systemPromptOverride, CancellationToken ct)
    {
        var repoPath = project.GitRepositoryUrl;

        stage.Status = StageStatus.Running;
        await _db.SaveChangesAsync(ct);

        var execution = new StageExecution
        {
            Id = Guid.NewGuid(),
            StageId = stage.Id,
            WorkflowId = workflow.Id,
            Version = stage.CurrentVersion,
            Status = StageStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _db.StageExecutions.Add(execution);
        await _db.SaveChangesAsync(ct);

        // Create stage branch for re-execution
        await _gitService.CreateStageBranchAsync(workflow.Id, stage.Name, repoPath, ct);

        // Gather upstream artifacts
        var upstreamArtifacts = await _db.StageExecutions
            .Where(se => se.WorkflowId == workflow.Id
                && se.Status == StageStatus.Completed
                && se.StageId != stage.Id)
            .OrderBy(se => se.StartedAt)
            .Select(se => se.GitTagName ?? string.Empty)
            .Where(tag => !string.IsNullOrEmpty(tag))
            .ToListAsync(ct);

        var context = new StageExecutionContext(
            WorkflowId: workflow.Id,
            StageId: stage.Id,
            StageName: stage.Name,
            ExecutorType: stage.ExecutorType,
            ModelName: stage.ModelName,
            SystemPrompt: systemPromptOverride ?? stage.Description,
            UpstreamArtifacts: upstreamArtifacts,
            Constitution: null,
            StageInstructions: stage.Description);

        StageExecutionResult result;
        try
        {
            result = await _stageExecutor.ExecuteAsync(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stage.Status = StageStatus.Failed;
            execution.Status = StageStatus.Failed;
            execution.CompletedAt = DateTime.UtcNow;
            execution.ErrorDetails = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        // Commit artifacts
        foreach (var artifactFilePath in result.ArtifactPaths)
        {
            await _gitService.CommitArtifactAsync(
                workflow.Id, stage.Name, result.OutputContent, artifactFilePath, repoPath, ct);
        }

        // Tag the new version (FR41 — version preserved)
        var tagName = await _gitService.TagStageAsync(
            workflow.Id, stage.Name, stage.CurrentVersion, repoPath, ct);

        execution.Status = StageStatus.Completed;
        execution.CompletedAt = DateTime.UtcNow;
        execution.GitTagName = tagName;

        stage.Status = StageStatus.Completed;
        stage.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToGroupAsync(
            $"workflow-{workflow.Id}",
            "StageCompleted",
            new { workflowId = workflow.Id, stageId = stage.Id, stageName = stage.Name, cascadeUpdate = true },
            ct);
    }

    private async Task RecordCascadeAuditAsync(
        Guid workflowId, Stage stage, CascadeAction action, CancellationToken ct)
    {
        // Record the cascade decision in a GateDecision for audit trail (FR42, FR53)
        var auditDecision = new GateDecision
        {
            Id = Guid.NewGuid(),
            StageId = stage.Id,
            WorkflowId = workflowId,
            Action = GateAction.GoBack,
            Feedback = $"Cascade action: {action} applied to stage \"{stage.Name}\" v{stage.CurrentVersion}.",
            DecidedBy = new Guid("a0000000-0000-0000-0000-000000000001"), // System user
            CreatedAt = DateTime.UtcNow
        };
        _db.GateDecisions.Add(auditDecision);
        await _db.SaveChangesAsync(ct);
    }

    private static string BuildDiffPatchPrompt(string upstreamStageName, string diffOutput)
    {
        return $"""
            The following changes were made to the upstream "{upstreamStageName}" artifact:

            {diffOutput}

            Please update this stage's artifact to reflect these upstream changes.
            Preserve the existing structure and content where it is not affected by the changes.
            Only modify sections that are directly impacted by the upstream diff.
            """;
    }
}

/// <summary>
/// Represents a downstream stage affected by a go-back course correction.
/// </summary>
public record AffectedStageDto(
    Guid StageId,
    string StageName,
    int StageOrder,
    int CurrentVersion,
    string Reason,
    CascadeAction DefaultAction);

/// <summary>
/// A user's cascade decision for a single affected stage.
/// </summary>
public record CascadeDecisionDto(
    Guid StageId,
    CascadeAction Action);

/// <summary>
/// Result of a go-back operation, containing the list of affected downstream stages.
/// </summary>
public record GoBackResult(
    IReadOnlyList<AffectedStageDto> AffectedStages);

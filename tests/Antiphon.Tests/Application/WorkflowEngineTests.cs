using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]
public class WorkflowEngineTests : TransactionalTestBase
{
    private readonly MockEventBus _eventBus = new();
    private readonly MockExecutor _mockExecutor = new();
    private readonly MockGitService _gitService = new();
    private WorkflowEngine _engine = null!;

    public WorkflowEngineTests(TestDbFixture fixture) : base(fixture)
    {
    }

    [Before(Test)]
    public new async Task SetupAsync()
    {
        await base.SetupAsync();
        var gitSettings = Options.Create(new GitSettings());
        var auditSettings = Options.Create(new AuditSettings());
        var auditService = new AuditService(DbContext, auditSettings);
        _engine = new WorkflowEngine(
            DbContext,
            _mockExecutor,
            _eventBus,
            _gitService,
            gitSettings,
            NullLogger<WorkflowEngine>.Instance,
            auditService);
    }

    private const string TwoStageYaml = """
        name: Test Workflow
        description: A test workflow with two stages
        stages:
          - name: Stage One
            executorType: mock
            gateRequired: false
            systemPrompt: Do stage one work
          - name: Stage Two
            executorType: mock
            gateRequired: false
            systemPrompt: Do stage two work
        """;

    private const string GatedWorkflowYaml = """
        name: Gated Workflow
        description: Workflow with a gate after stage one
        stages:
          - name: Stage One
            executorType: mock
            gateRequired: true
            systemPrompt: Produce first artifact
          - name: Stage Two
            executorType: mock
            gateRequired: false
            systemPrompt: Produce second artifact
        """;

    private const string ThreeStageGatedYaml = """
        name: Three Stage Gated
        description: Three stages with gate on second
        stages:
          - name: Stage One
            executorType: mock
            gateRequired: false
          - name: Stage Two
            executorType: mock
            gateRequired: true
          - name: Stage Three
            executorType: mock
            gateRequired: false
        """;

    private async Task<(Guid TemplateId, Guid ProjectId)> SeedTemplateAndProjectAsync(string yaml)
    {
        var template = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Test Template",
            Description = "For tests",
            YamlDefinition = yaml,
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        DbContext.WorkflowTemplates.Add(template);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Test Project",
            GitRepositoryUrl = "https://example.com/repo.git",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        DbContext.Projects.Add(project);

        await DbContext.SaveChangesAsync();
        return (template.Id, project.Id);
    }

    #region YAML Parsing

    [Test]
    public void ParseYamlDefinition_ValidYaml_ReturnsWorkflowDefinition()
    {
        var definition = WorkflowEngine.ParseYamlDefinition(TwoStageYaml);

        definition.Name.ShouldBe("Test Workflow");
        definition.Description.ShouldBe("A test workflow with two stages");
        definition.Stages.Count().ShouldBe(2);
        definition.Stages[0].Name.ShouldBe("Stage One");
        definition.Stages[0].ExecutorType.ShouldBe("mock");
        definition.Stages[0].GateRequired.ShouldBeFalse();
        definition.Stages[0].SystemPrompt.ShouldBe("Do stage one work");
        definition.Stages[1].Name.ShouldBe("Stage Two");
    }

    [Test]
    public void ParseYamlDefinition_GateRequired_ParsedCorrectly()
    {
        var definition = WorkflowEngine.ParseYamlDefinition(GatedWorkflowYaml);

        definition.Stages[0].GateRequired.ShouldBeTrue();
        definition.Stages[1].GateRequired.ShouldBeFalse();
    }

    [Test]
    public void ParseYamlDefinition_EmptyYaml_ThrowsValidationException()
    {
        var act = () => WorkflowEngine.ParseYamlDefinition("");

        Should.Throw<ValidationException>(act);
    }

    [Test]
    public void ParseYamlDefinition_NoStages_ThrowsValidationException()
    {
        var yaml = "name: No stages workflow";
        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        Should.Throw<ValidationException>(act);
    }

    [Test]
    public void ParseYamlDefinition_WithModelName_ParsedCorrectly()
    {
        var yaml = """
            name: Model Workflow
            stages:
              - name: Stage One
                executorType: ai-agent
                modelName: claude-opus
                gateRequired: false
            """;

        var definition = WorkflowEngine.ParseYamlDefinition(yaml);

        definition.Stages[0].ModelName.ShouldBe("claude-opus");
    }

    #endregion

    #region CreateWorkflowAsync

    [Test]
    public async Task CreateWorkflowAsync_ValidInputs_CreatesWorkflowAndStages()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        var workflow = await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "initial context", null, null, null), CancellationToken.None);

        workflow.Name.ShouldBe("Test Workflow");
        workflow.TemplateId.ShouldBe(templateId);
        workflow.ProjectId.ShouldBe(projectId);
        workflow.InitialContext.ShouldBe("initial context");

        // Verify stages were created
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();

        stages.Count().ShouldBe(2);
        stages[0].Name.ShouldBe("Stage One");
        stages[0].StageOrder.ShouldBe(0);
        stages[1].Name.ShouldBe("Stage Two");
        stages[1].StageOrder.ShouldBe(1);
    }

    [Test]
    public async Task CreateWorkflowAsync_NoGates_CompletesFullExecution()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        var workflow = await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        // Reload to get latest state
        var refreshed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        refreshed.Status.ShouldBe(WorkflowStatus.Completed);
        refreshed.CompletedAt.ShouldNotBeNull();

        // All stages should be completed
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .ToListAsync();
        foreach (var s in stages)
        {
            s.Status.ShouldBe(StageStatus.Completed);
        }
    }

    [Test]
    public async Task CreateWorkflowAsync_InvalidTemplate_ThrowsNotFoundException()
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "P",
            GitRepositoryUrl = "https://example.com/repo.git",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        DbContext.Projects.Add(project);
        await DbContext.SaveChangesAsync();

        var act = () => _engine.CreateWorkflowAsync(new CreateWorkflowRequest(Guid.NewGuid(), project.Id, null, "ctx", null, null, null), CancellationToken.None);

        await Should.ThrowAsync<NotFoundException>(act);
    }

    #endregion

    #region ExecuteNextStageAsync & MockExecutor

    [Test]
    public async Task ExecuteNextStageAsync_MockExecutor_ProducesPlaceholderArtifact()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        // Verify stage executions were created
        var executions = await DbContext.StageExecutions
            .Where(se => se.Status == StageStatus.Completed)
            .ToListAsync();

        executions.Count().ShouldBe(2);
        foreach (var e in executions)
        {
            e.GitTagName.ShouldNotBeNullOrEmpty();
            e.CompletedAt.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task ExecuteNextStageAsync_PushesStageStartedAndCompletedEvents()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        var stageStartedEvents = _eventBus.PublishedEvents
            .Where(e => e.EventName == "StageStarted")
            .ToList();

        var stageCompletedEvents = _eventBus.PublishedEvents
            .Where(e => e.EventName == "StageCompleted")
            .ToList();

        stageStartedEvents.Count().ShouldBe(2);
        stageCompletedEvents.Count().ShouldBe(2);
    }

    #endregion

    #region Gate Pausing

    [Test]
    public async Task CreateWorkflowAsync_GateRequired_PausesAtGateWaiting()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(GatedWorkflowYaml);

        var workflow = await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        var refreshed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        refreshed.Status.ShouldBe(WorkflowStatus.GateWaiting);

        // Only the first stage should be completed
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();

        stages[0].Status.ShouldBe(StageStatus.Completed);
        stages[1].Status.ShouldBe(StageStatus.Pending);
    }

    [Test]
    public async Task CreateWorkflowAsync_GateRequired_PushesGateReadyEvent()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(GatedWorkflowYaml);

        await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        var gateReadyEvents = _eventBus.PublishedEvents
            .Where(e => e.EventName == "GateReady")
            .ToList();

        gateReadyEvents.Count().ShouldBe(1);
    }

    #endregion

    #region ResumeAfterGateAsync

    [Test]
    public async Task ResumeAfterGateAsync_CompletesRemainingStages()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(GatedWorkflowYaml);

        var workflow = await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        // Should be paused at gate
        var paused = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        paused.Status.ShouldBe(WorkflowStatus.GateWaiting);

        // Resume after gate approval
        await _engine.ResumeAfterGateAsync(workflow.Id, CancellationToken.None);

        var completed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        completed.Status.ShouldBe(WorkflowStatus.Completed);

        // All stages should be completed now
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .ToListAsync();
        foreach (var s in stages)
        {
            s.Status.ShouldBe(StageStatus.Completed);
        }
    }

    [Test]
    public async Task ResumeAfterGateAsync_NotGateWaiting_ThrowsConflict()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        // This workflow has no gates, so it will complete immediately
        var workflow = await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "context", null, null, null), CancellationToken.None);

        var act = () => _engine.ResumeAfterGateAsync(workflow.Id, CancellationToken.None);

        await Should.ThrowAsync<ConflictException>(act);
    }

    #endregion

    #region Full Cycle: Create → Execute → Gate → Approve → Next Stage

    [Test]
    public async Task FullCycle_ThreeStagesWithGate_ExecutesCorrectly()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(ThreeStageGatedYaml);

        // Create workflow — should execute stage 1 (no gate), execute stage 2 (gate), pause
        var workflow = await _engine.CreateWorkflowAsync(new CreateWorkflowRequest(templateId, projectId, null, "full cycle test", null, null, null), CancellationToken.None);

        var paused = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        paused.Status.ShouldBe(WorkflowStatus.GateWaiting);

        var stagesAfterPause = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();

        stagesAfterPause[0].Status.ShouldBe(StageStatus.Completed, "Stage One should be completed (no gate)");
        stagesAfterPause[1].Status.ShouldBe(StageStatus.Completed, "Stage Two should be completed (gate pauses after execution)");
        stagesAfterPause[2].Status.ShouldBe(StageStatus.Pending, "Stage Three should still be pending");

        // Clear events to track only resume events
        _eventBus.Clear();

        // Resume after gate
        await _engine.ResumeAfterGateAsync(workflow.Id, CancellationToken.None);

        var completed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        completed.Status.ShouldBe(WorkflowStatus.Completed);

        var allStages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .ToListAsync();
        foreach (var s in allStages)
        {
            s.Status.ShouldBe(StageStatus.Completed);
        }

        // Verify events after resume: WorkflowStatusChanged(Running), StageStarted, StageCompleted, WorkflowCompleted
        _eventBus.PublishedEvents.ShouldContain(e => e.EventName == "WorkflowStatusChanged");
        _eventBus.PublishedEvents.ShouldContain(e => e.EventName == "StageStarted");
        _eventBus.PublishedEvents.ShouldContain(e => e.EventName == "StageCompleted");
        _eventBus.PublishedEvents.ShouldContain(e => e.EventName == "WorkflowCompleted");
    }

    #endregion
}

using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Antiphon.Tests.Application;

[Collection("Database")]
public class WorkflowEngineTests : TransactionalTestBase
{
    private readonly MockEventBus _eventBus = new();
    private readonly MockExecutor _mockExecutor = new();
    private readonly WorkflowEngine _engine;

    public WorkflowEngineTests(TestDbFixture fixture) : base(fixture)
    {
        _engine = new WorkflowEngine(DbContext, _mockExecutor, _eventBus);
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

    [Fact]
    public void ParseYamlDefinition_ValidYaml_ReturnsWorkflowDefinition()
    {
        var definition = WorkflowEngine.ParseYamlDefinition(TwoStageYaml);

        definition.Name.Should().Be("Test Workflow");
        definition.Description.Should().Be("A test workflow with two stages");
        definition.Stages.Should().HaveCount(2);
        definition.Stages[0].Name.Should().Be("Stage One");
        definition.Stages[0].ExecutorType.Should().Be("mock");
        definition.Stages[0].GateRequired.Should().BeFalse();
        definition.Stages[0].SystemPrompt.Should().Be("Do stage one work");
        definition.Stages[1].Name.Should().Be("Stage Two");
    }

    [Fact]
    public void ParseYamlDefinition_GateRequired_ParsedCorrectly()
    {
        var definition = WorkflowEngine.ParseYamlDefinition(GatedWorkflowYaml);

        definition.Stages[0].GateRequired.Should().BeTrue();
        definition.Stages[1].GateRequired.Should().BeFalse();
    }

    [Fact]
    public void ParseYamlDefinition_EmptyYaml_ThrowsValidationException()
    {
        var act = () => WorkflowEngine.ParseYamlDefinition("");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void ParseYamlDefinition_NoStages_ThrowsValidationException()
    {
        var yaml = "name: No stages workflow";
        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
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

        definition.Stages[0].ModelName.Should().Be("claude-opus");
    }

    #endregion

    #region CreateWorkflowAsync

    [Fact]
    public async Task CreateWorkflowAsync_ValidInputs_CreatesWorkflowAndStages()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        var workflow = await _engine.CreateWorkflowAsync(templateId, projectId, "initial context", CancellationToken.None);

        workflow.Name.Should().Be("Test Workflow");
        workflow.TemplateId.Should().Be(templateId);
        workflow.ProjectId.Should().Be(projectId);
        workflow.InitialContext.Should().Be("initial context");

        // Verify stages were created
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();

        stages.Should().HaveCount(2);
        stages[0].Name.Should().Be("Stage One");
        stages[0].StageOrder.Should().Be(0);
        stages[1].Name.Should().Be("Stage Two");
        stages[1].StageOrder.Should().Be(1);
    }

    [Fact]
    public async Task CreateWorkflowAsync_NoGates_CompletesFullExecution()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        var workflow = await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        // Reload to get latest state
        var refreshed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        refreshed.Status.Should().Be(WorkflowStatus.Completed);
        refreshed.CompletedAt.Should().NotBeNull();

        // All stages should be completed
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .ToListAsync();
        stages.Should().AllSatisfy(s => s.Status.Should().Be(StageStatus.Completed));
    }

    [Fact]
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

        var act = () => _engine.CreateWorkflowAsync(Guid.NewGuid(), project.Id, "ctx", CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    #endregion

    #region ExecuteNextStageAsync & MockExecutor

    [Fact]
    public async Task ExecuteNextStageAsync_MockExecutor_ProducesPlaceholderArtifact()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        // Verify stage executions were created
        var executions = await DbContext.StageExecutions
            .Where(se => se.Status == StageStatus.Completed)
            .ToListAsync();

        executions.Should().HaveCount(2);
        executions.Should().AllSatisfy(e =>
        {
            e.GitTagName.Should().NotBeNullOrEmpty();
            e.CompletedAt.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task ExecuteNextStageAsync_PushesStageStartedAndCompletedEvents()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        var stageStartedEvents = _eventBus.PublishedEvents
            .Where(e => e.EventName == "StageStarted")
            .ToList();

        var stageCompletedEvents = _eventBus.PublishedEvents
            .Where(e => e.EventName == "StageCompleted")
            .ToList();

        stageStartedEvents.Should().HaveCount(2);
        stageCompletedEvents.Should().HaveCount(2);
    }

    #endregion

    #region Gate Pausing

    [Fact]
    public async Task CreateWorkflowAsync_GateRequired_PausesAtGateWaiting()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(GatedWorkflowYaml);

        var workflow = await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        var refreshed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        refreshed.Status.Should().Be(WorkflowStatus.GateWaiting);

        // Only the first stage should be completed
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();

        stages[0].Status.Should().Be(StageStatus.Completed);
        stages[1].Status.Should().Be(StageStatus.Pending);
    }

    [Fact]
    public async Task CreateWorkflowAsync_GateRequired_PushesGateReadyEvent()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(GatedWorkflowYaml);

        await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        var gateReadyEvents = _eventBus.PublishedEvents
            .Where(e => e.EventName == "GateReady")
            .ToList();

        gateReadyEvents.Should().HaveCount(1);
    }

    #endregion

    #region ResumeAfterGateAsync

    [Fact]
    public async Task ResumeAfterGateAsync_CompletesRemainingStages()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(GatedWorkflowYaml);

        var workflow = await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        // Should be paused at gate
        var paused = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        paused.Status.Should().Be(WorkflowStatus.GateWaiting);

        // Resume after gate approval
        await _engine.ResumeAfterGateAsync(workflow.Id, CancellationToken.None);

        var completed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        completed.Status.Should().Be(WorkflowStatus.Completed);

        // All stages should be completed now
        var stages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .ToListAsync();
        stages.Should().AllSatisfy(s => s.Status.Should().Be(StageStatus.Completed));
    }

    [Fact]
    public async Task ResumeAfterGateAsync_NotGateWaiting_ThrowsConflict()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(TwoStageYaml);

        // This workflow has no gates, so it will complete immediately
        var workflow = await _engine.CreateWorkflowAsync(templateId, projectId, "context", CancellationToken.None);

        var act = () => _engine.ResumeAfterGateAsync(workflow.Id, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    #endregion

    #region Full Cycle: Create → Execute → Gate → Approve → Next Stage

    [Fact]
    public async Task FullCycle_ThreeStagesWithGate_ExecutesCorrectly()
    {
        var (templateId, projectId) = await SeedTemplateAndProjectAsync(ThreeStageGatedYaml);

        // Create workflow — should execute stage 1 (no gate), execute stage 2 (gate), pause
        var workflow = await _engine.CreateWorkflowAsync(templateId, projectId, "full cycle test", CancellationToken.None);

        var paused = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        paused.Status.Should().Be(WorkflowStatus.GateWaiting);

        var stagesAfterPause = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .OrderBy(s => s.StageOrder)
            .ToListAsync();

        stagesAfterPause[0].Status.Should().Be(StageStatus.Completed, "Stage One should be completed (no gate)");
        stagesAfterPause[1].Status.Should().Be(StageStatus.Completed, "Stage Two should be completed (gate pauses after execution)");
        stagesAfterPause[2].Status.Should().Be(StageStatus.Pending, "Stage Three should still be pending");

        // Clear events to track only resume events
        _eventBus.Clear();

        // Resume after gate
        await _engine.ResumeAfterGateAsync(workflow.Id, CancellationToken.None);

        var completed = await DbContext.Workflows.FirstAsync(w => w.Id == workflow.Id);
        completed.Status.Should().Be(WorkflowStatus.Completed);

        var allStages = await DbContext.Stages
            .Where(s => s.WorkflowId == workflow.Id)
            .ToListAsync();
        allStages.Should().AllSatisfy(s => s.Status.Should().Be(StageStatus.Completed));

        // Verify events after resume: WorkflowStatusChanged(Running), StageStarted, StageCompleted, WorkflowCompleted
        _eventBus.PublishedEvents.Should().Contain(e => e.EventName == "WorkflowStatusChanged");
        _eventBus.PublishedEvents.Should().Contain(e => e.EventName == "StageStarted");
        _eventBus.PublishedEvents.Should().Contain(e => e.EventName == "StageCompleted");
        _eventBus.PublishedEvents.Should().Contain(e => e.EventName == "WorkflowCompleted");
    }

    #endregion
}

using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents;
using FluentAssertions;
using Xunit;

namespace Antiphon.Tests.Application;

/// <summary>
/// Unit tests for WorkflowEngine YAML parsing and MockExecutor — no database required.
/// </summary>
public class WorkflowEngineParsingTests
{
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

    [Fact]
    public void ParseYamlDefinition_MissingStageName_ThrowsValidationException()
    {
        var yaml = """
            name: Bad Workflow
            stages:
              - executorType: mock
                gateRequired: false
            """;

        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void ParseYamlDefinition_MissingExecutorType_ThrowsValidationException()
    {
        var yaml = """
            name: Bad Workflow
            stages:
              - name: Stage One
                gateRequired: false
            """;

        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void ParseYamlDefinition_EmptyStagesArray_ThrowsValidationException()
    {
        var yaml = """
            name: Empty Stages
            stages: []
            """;

        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        act.Should().Throw<ValidationException>();
    }

    #endregion

    #region MockExecutor

    [Fact]
    public async Task MockExecutor_ProducesPlaceholderOutput()
    {
        var executor = new MockExecutor();
        var context = new StageExecutionContext(
            WorkflowId: Guid.NewGuid(),
            StageId: Guid.NewGuid(),
            StageName: "Architecture",
            ExecutorType: "mock",
            ModelName: "claude-opus",
            SystemPrompt: "Design the architecture",
            UpstreamArtifacts: [],
            Constitution: null,
            StageInstructions: "Design the architecture");

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        result.OutputContent.Should().Contain("Architecture Output");
        result.OutputContent.Should().Contain("MockExecutor");
        result.ArtifactPaths.Should().HaveCount(1);
        result.ArtifactPaths[0].Should().Contain("Architecture.md");
        result.SuggestedActions.Should().BeNull();
    }

    [Fact]
    public async Task MockExecutor_RespectsCalcellationToken()
    {
        var executor = new MockExecutor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new StageExecutionContext(
            WorkflowId: Guid.NewGuid(),
            StageId: Guid.NewGuid(),
            StageName: "Test",
            ExecutorType: "mock",
            ModelName: null,
            SystemPrompt: null,
            UpstreamArtifacts: [],
            Constitution: null,
            StageInstructions: null);

        var act = () => executor.ExecuteAsync(context, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}

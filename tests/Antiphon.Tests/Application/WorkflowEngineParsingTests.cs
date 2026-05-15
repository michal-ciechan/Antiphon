using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Unit tests for WorkflowEngine YAML parsing and MockExecutor — no database required.
/// </summary>
[Category("Unit")]
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

    [Test]
    public void ParseYamlDefinition_WithHooks_ParsesWorkspaceHookBlock()
    {
        var yaml = """
            name: Hooked Workflow
            hooks:
              after_create:
                command: dotnet restore
                timeout_seconds: 45
              before_run: echo before
              after_run:
                command: echo after
                timeoutSeconds: 5
              before_remove:
                command: echo remove
            stages:
              - name: Stage One
                executorType: mock
                gateRequired: false
            """;

        var definition = WorkflowEngine.ParseYamlDefinition(yaml);

        definition.Hooks.AfterCreate.ShouldNotBeNull();
        definition.Hooks.AfterCreate.Command.ShouldBe("dotnet restore");
        definition.Hooks.AfterCreate.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        definition.Hooks.BeforeRun.ShouldNotBeNull();
        definition.Hooks.BeforeRun.Command.ShouldBe("echo before");
        definition.Hooks.BeforeRun.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        definition.Hooks.AfterRun.ShouldNotBeNull();
        definition.Hooks.AfterRun.Command.ShouldBe("echo after");
        definition.Hooks.AfterRun.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
        definition.Hooks.BeforeRemove.ShouldNotBeNull();
        definition.Hooks.BeforeRemove.Command.ShouldBe("echo remove");
    }

    [Test]
    public void ParseYamlDefinition_InvalidHooks_ThrowValidationException()
    {
        var invalidHookBlocks = new[]
        {
            """
            hooks:
              - before_run
            """,
            """
            hooks:
              beforeRun:
                command: echo before
            """,
            """
            hooks:
              before_run:
                command: ""
            """,
            """
            hooks:
              before_run:
                command: echo before
                timeout_seconds: 0
            """,
            """
            hooks:
              before_run:
                command: echo before
                timeout_seconds: slow
            """,
            """
            hooks:
              before_run:
                command: echo before
                timeout_seconds:
                  - 30
            """,
            """
            hooks:
              before_run:
                - echo before
            """
        };

        foreach (var hookBlock in invalidHookBlocks)
        {
            var yaml = $"""
                name: Bad Hook Workflow
                {hookBlock}
                stages:
                  - name: Stage One
                    executorType: mock
                """;

            var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

            Should.Throw<ValidationException>(act);
        }
    }

    [Test]
    public void ValidateYamlStructure_ValidatesHookBlocks()
    {
        var validYaml = """
            name: Valid Hook Workflow
            hooks:
              before_run:
                command: echo before
                timeout_seconds: 30
              after_run: echo after
            stages:
              - name: Stage One
                executorType: mock
            """;
        var invalidYaml = """
            name: Invalid Hook Workflow
            hooks:
              beforeRun:
                command: echo typo
              before_run:
                command: echo before
                timeout_seconds:
                  - 30
              after_run:
                command: ""
            stages:
              - name: Stage One
                executorType: mock
            """;

        WorkflowTemplateService.ValidateYamlStructure(validYaml).ShouldBeEmpty();

        var errors = WorkflowTemplateService.ValidateYamlStructure(invalidYaml);
        errors.ShouldContain("Unknown hook 'beforeRun'.");
        errors.ShouldContain("Hook 'before_run' timeout_seconds must be a scalar value.");
        errors.ShouldContain("Hook 'after_run' must define a non-empty command.");
    }

    [Test]
    public void ParseYamlDefinition_MissingStageName_ThrowsValidationException()
    {
        var yaml = """
            name: Bad Workflow
            stages:
              - executorType: mock
                gateRequired: false
            """;

        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        Should.Throw<ValidationException>(act);
    }

    [Test]
    public void ParseYamlDefinition_MissingExecutorType_ThrowsValidationException()
    {
        var yaml = """
            name: Bad Workflow
            stages:
              - name: Stage One
                gateRequired: false
            """;

        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        Should.Throw<ValidationException>(act);
    }

    [Test]
    public void ParseYamlDefinition_EmptyStagesArray_ThrowsValidationException()
    {
        var yaml = """
            name: Empty Stages
            stages: []
            """;

        var act = () => WorkflowEngine.ParseYamlDefinition(yaml);

        Should.Throw<ValidationException>(act);
    }

    #endregion

    #region MockExecutor

    [Test]
    public async Task MockExecutor_ProducesPlaceholderOutput()
    {
        var executor = new MockExecutor();
        var context = new StageExecutionContext(
            WorkflowId: Guid.NewGuid(),
            StageId: Guid.NewGuid(),
            StageExecutionId: Guid.NewGuid(),
            StageName: "Architecture",
            ExecutorType: "mock",
            ModelName: "claude-opus",
            SystemPrompt: "Design the architecture",
            UpstreamArtifacts: [],
            Constitution: null,
            StageInstructions: "Design the architecture",
            InitialContext: null,
            BranchName: null);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        result.OutputContent.ShouldContain("Architecture Output");
        result.OutputContent.ShouldContain("MockExecutor");
        result.ArtifactPaths.Count.ShouldBe(1);
        result.ArtifactPaths[0].ShouldContain("Architecture.md");
        result.SuggestedActions.ShouldBeNull();
    }

    [Test]
    public async Task MockExecutor_RespectsCalcellationToken()
    {
        var executor = new MockExecutor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new StageExecutionContext(
            WorkflowId: Guid.NewGuid(),
            StageId: Guid.NewGuid(),
            StageExecutionId: Guid.NewGuid(),
            StageName: "Test",
            ExecutorType: "mock",
            ModelName: null,
            SystemPrompt: null,
            UpstreamArtifacts: [],
            Constitution: null,
            StageInstructions: null,
            InitialContext: null,
            BranchName: null);

        var act = () => executor.ExecuteAsync(context, cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(act);
    }

    #endregion
}

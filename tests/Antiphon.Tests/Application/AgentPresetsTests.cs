using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0255 — create-time preset apply. Pure; no database.</summary>
public class AgentPresetsTests
{
    [Test]
    public void apply_omit_takes_the_orchestrator_preset()
    {
        var applied = Apply(AgentPresets.Orchestrator);

        applied.AlwaysOn.ShouldBeTrue();
        applied.RemoteControlEnabled.ShouldBeTrue();
        applied.ModelLevel.ShouldBe(AgentModelLevel.High);
        applied.ReplyStyle.ShouldBe(AgentReplyStyle.Normal);
        applied.BundleKeys.ShouldBe([InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);
        applied.DefaultWorkflowTemplateId.ShouldBe(AgentPresets.FullFeaturePipelineTemplateId);
        applied.SystemPromptAppend.ShouldNotBeNull();
        applied.SystemPromptAppend!.ShouldContain("Orchestra");
        applied.SystemPromptAppend.ShouldContain("The Board");
        applied.SystemPromptAppend.ShouldContain("C:/src/app");
        applied.SystemPromptAppend.ShouldNotContain("{project}");
        applied.SystemPromptAppend.ShouldNotContain("{board}");
        applied.SystemPromptAppend.ShouldNotContain("{directory}");
    }

    [Test]
    public void apply_explicit_false_always_on_and_remote_control_wins()
    {
        var applied = Apply(
            AgentPresets.Orchestrator,
            alwaysOn: false,
            remoteControlEnabled: false);

        applied.AlwaysOn.ShouldBeFalse();
        applied.RemoteControlEnabled.ShouldBeFalse();
        applied.BundleKeys.ShouldBe([InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);
    }

    [Test]
    public void apply_explicit_empty_bundle_keys_wins()
    {
        var applied = Apply(AgentPresets.Orchestrator, bundleKeys: []);

        applied.BundleKeys.ShouldBe([]);
        applied.AlwaysOn.ShouldBeTrue();
    }

    [Test]
    public void apply_explicit_prompt_skips_render()
    {
        var applied = Apply(AgentPresets.Orchestrator, systemPromptAppend: "Custom contract.");

        applied.SystemPromptAppend.ShouldBe("Custom contract.");
    }

    [Test]
    public void apply_null_prompt_renders_project_board_and_directory()
    {
        var applied = Apply(AgentPresets.Orchestrator, systemPromptAppend: null);

        applied.SystemPromptAppend.ShouldNotBeNull();
        applied.SystemPromptAppend!.ShouldContain("Orchestra");
        applied.SystemPromptAppend.ShouldContain("The Board");
        applied.SystemPromptAppend.ShouldContain("C:/src/app");
        applied.SystemPromptAppend.ShouldNotContain("{project}");
    }

    [Test]
    public void apply_unknown_key_throws_preset()
    {
        var ex = Should.Throw<ValidationException>(() => Apply("not-a-preset"));

        ex.Errors.ContainsKey("preset").ShouldBeTrue();
        ex.Errors["preset"].ShouldContain(message => message.Contains("not-a-preset"));
    }

    [Test]
    public void apply_no_preset_uses_the_hard_defaults()
    {
        var applied = Apply(null);

        applied.AlwaysOn.ShouldBeFalse();
        applied.RemoteControlEnabled.ShouldBeFalse();
        applied.BundleKeys.ShouldBeNull();
        applied.SystemPromptAppend.ShouldBeNull();
        applied.DefaultWorkflowTemplateId.ShouldBeNull();
        applied.ModelLevel.ShouldBe(AgentModelLevel.High);
    }

    private static AppliedAgentPreset Apply(
        string? presetKey,
        bool? alwaysOn = null,
        bool? remoteControlEnabled = null,
        IReadOnlyList<string>? bundleKeys = null,
        string? systemPromptAppend = null)
        => AgentPresets.Apply(
            presetKey,
            modelLevel: null,
            replyStyle: null,
            alwaysOn,
            remoteControlEnabled,
            bundleKeys,
            systemPromptAppend,
            defaultWorkflowTemplateId: null,
            project: "Orchestra",
            board: "The Board",
            repoUrl: "https://example.com/repo.git",
            directory: "C:/src/app");
}

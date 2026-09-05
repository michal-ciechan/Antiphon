using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0182 T11 — structurally, only the resolver (profile path) or AgentRegistry (no-profile
/// path) may emit --model. These two services used to append it themselves.
/// </summary>
[Category("Unit")]
public sealed class LaunchModelArgumentAppenderTests
{
    [Test]
    public void ForLaunch_maps_supported_ladders_and_omits_unsupported_kinds()
    {
        ModelLevelAliases.ForLaunch(AgentKind.ClaudeCode, AgentModelLevel.Frontier).ShouldBe("fable");
        ModelLevelAliases.ForLaunch(AgentKind.ClaudeCode, AgentModelLevel.High).ShouldBe("opus");
        ModelLevelAliases.ForLaunch(AgentKind.ClaudeCode, AgentModelLevel.Medium).ShouldBe("sonnet");
        ModelLevelAliases.ForLaunch(AgentKind.ClaudeCode, AgentModelLevel.Low).ShouldBe("haiku");
        ModelLevelAliases.ForLaunch(AgentKind.Codex, AgentModelLevel.Frontier).ShouldBe("gpt-6-astra");
        ModelLevelAliases.ForLaunch(AgentKind.Codex, AgentModelLevel.High).ShouldBe("gpt-5.6-sol");
        ModelLevelAliases.ForLaunch(AgentKind.Codex, AgentModelLevel.Medium).ShouldBe("gpt-5.6-terra");
        ModelLevelAliases.ForLaunch(AgentKind.Codex, AgentModelLevel.Low).ShouldBe("gpt-5.6-luna");
        ModelLevelAliases.ForLaunch(AgentKind.Grok, AgentModelLevel.Low).ShouldBe("grok-4.6");
        ModelLevelAliases.ForLaunch(AgentKind.Raw, AgentModelLevel.High).ShouldBeNull();
        ModelLevelAliases.ForLaunch(AgentKind.OpenCode, AgentModelLevel.High).ShouldBeNull();
    }

    [Test]
    public void T11_control_and_dispatcher_never_emit_the_model_flag()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var control = File.ReadAllText(Path.Combine(
            root, "server", "Application", "Services", "AgentControlService.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(
            root, "server", "Application", "Services", "AgentTaskDispatcher.cs"));

        control.ShouldNotContain("\"--model\"");
        dispatcher.ShouldNotContain("\"--model\"");
    }
}

using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0182 T11 — structurally, only the resolver (profile path) or AgentRegistry (no-profile
/// path) may emit --model. These two services used to append it themselves.
/// </summary>
public sealed class LaunchModelArgumentAppenderTests
{
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

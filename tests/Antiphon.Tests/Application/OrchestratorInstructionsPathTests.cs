using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class OrchestratorInstructionsPathTests
{
    [Test]
    [Arguments("windows")]
    [Arguments("linux")]
    public void Default_path_uses_the_platform_data_root_and_a_relative_override_is_refused(string platform)
    {
        if (platform == "windows")
        {
            var environment = new AgentTuiPathEnvironment(
                AgentTuiPlatform.Windows,
                @"C:\Users\op\AppData\Local",
                null,
                null);
            AntiphonDataPaths.ResolveOrchestratorInstructionsPath(environment, null).ShouldBe(
                @"C:\Users\op\AppData\Local\Antiphon\orchestrator\ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md");
            Should.Throw<InvalidOperationException>(() =>
                AntiphonDataPaths.ResolveOrchestratorInstructionsPath(environment, "relative\\file.md"));
        }
        else
        {
            var environment = new AgentTuiPathEnvironment(
                AgentTuiPlatform.Linux,
                null,
                "",
                "/home/op");
            AntiphonDataPaths.ResolveOrchestratorInstructionsPath(environment, null).ShouldBe(
                "/home/op/.local/share/antiphon/orchestrator/ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md");
            Should.Throw<InvalidOperationException>(() =>
                AntiphonDataPaths.ResolveOrchestratorInstructionsPath(environment, "relative/file.md"));
        }
    }
}

using System.Net;
using Antiphon.Agents.Pty;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class QualificationGroupTwoRegressionTests
{
    [Test]
    public async Task Startup_diagnostics_dependency_does_not_poison_runner_routes()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        using var response = await host.Http.GetAsync("/api/version");
        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            string.Join("\n", host.Logs.Entries.Select(e => e.Exception?.ToString() ?? e.Message)));
        (await host.RegisterAsync()).Epoch.ShouldBeGreaterThan(0);
    }

    [Test, Arguments("", "high"), Arguments("zzprobe", "xhigh")]
    public void Effort_fixture_composer_is_readable_to_the_startup_input_guard(string content, string effort)
    {
        var screen = EffortFixtureComposer.Screen(content, effort);
        ClaudeDialogObservation.Observe(screen).State.ShouldBe(ClaudeDialogState.Clear);
        ClaudeScreen.TryReadComposer(screen, out var read).ShouldBeTrue();
        read.ShouldBe(content);
    }
}

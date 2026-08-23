using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0161: herdr's agent.prompt must never appear on the typed client surface — S1 measured a
/// false agent_prompt_stalled on a successful delivery.
/// </summary>
public class HerdrClientSurfaceTests
{
    [Test]
    public void HerdrClient_public_surface_never_sends_agent_prompt()
    {
        var methods = typeof(HerdrClient).GetMethods()
            .Where(m => m.DeclaringType == typeof(HerdrClient) && m.IsPublic)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        methods.ShouldNotContain("AgentPromptAsync");
        methods.Any(n => n.Contains("Prompt", StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("Report", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse("no Prompt* wrapper may call herdr agent.prompt");

        // Positive pin: the S2 typed surface we rely on is present.
        methods.ShouldContain("PaneSendTextAsync");
        methods.ShouldContain("PaneSendKeysAsync");
        methods.ShouldContain("PaneGetAsync");
        methods.ShouldContain("AgentStartAsync");
    }
}

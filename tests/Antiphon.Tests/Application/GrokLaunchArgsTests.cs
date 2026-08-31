using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0289 — Grok's launch-time reasoning-effort flag, measured against grok CLI 1.0.13 on
/// 2026-08-31. The catalog for grok-4.6 is xhigh / high / medium / low; grok-4.5 has no xhigh
/// and the CLI refuses to launch with it rather than degrading.
/// </summary>
[Category("Unit")]
public class GrokLaunchArgsTests
{
    private static readonly string[] Grok46Catalog = ["xhigh", "high", "medium", "low"];

    [Test]
    [Arguments(AgentModelLevel.Frontier, "xhigh")]
    [Arguments(AgentModelLevel.High, "high")]
    [Arguments(AgentModelLevel.Medium, "medium")]
    [Arguments(AgentModelLevel.Low, "low")]
    public void every_tier_names_its_own_reasoning_effort(AgentModelLevel level, string expected)
    {
        GrokLaunchArgs.ReasoningEffort(level).ShouldBe(expected);
        Grok46Catalog.ShouldContain(expected);
    }

    [Test]
    public void the_effort_ladder_is_monotonic_with_the_tier_ladder()
    {
        var order = new[] { "low", "medium", "high", "xhigh" };
        var depths = Enum.GetValues<AgentModelLevel>()
            .OrderBy(level => (int)level)
            .Select(level => Array.IndexOf(order, GrokLaunchArgs.ReasoningEffort(level)))
            .ToList();

        depths.ShouldAllBe(d => d >= 0, "every effort must be in grok-4.6's catalog");
        depths.ShouldBe(depths.OrderByDescending(d => d).ToList());
    }

    [Test]
    public void every_emitted_effort_is_in_the_grok_4_6_catalog()
    {
        foreach (var level in Enum.GetValues<AgentModelLevel>())
            Grok46Catalog.ShouldContain(GrokLaunchArgs.ReasoningEffort(level));
    }

    [Test]
    public void the_canonical_flag_is_reasoning_effort_not_the_effort_alias()
    {
        GrokLaunchArgs.ReasoningEffortFlag.ShouldBe("--reasoning-effort");
        GrokLaunchArgs.ReasoningEffortFlag.ShouldNotBe("--effort");
    }

    [Test]
    public void grok_4_5_clamps_frontier_xhigh_to_high_and_leaves_other_tiers_alone()
    {
        // Live probe 2026-08-31: grok -m grok-4.5 --reasoning-effort xhigh exits 1 with
        // "unknown effort level 'xhigh'; use one of: high, medium, low".
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Frontier, "grok-4.5").ShouldBe("high");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.High, "grok-4.5").ShouldBe("high");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Medium, "grok-4.5").ShouldBe("medium");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Low, "grok-4.5").ShouldBe("low");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Frontier, "GROK-4.5").ShouldBe("high");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Frontier, " grok-4.5 ").ShouldBe("high");
    }

    [Test]
    public void grok_4_6_and_an_unset_model_keep_the_table_including_xhigh()
    {
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Frontier, null).ShouldBe("xhigh");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Frontier, "").ShouldBe("xhigh");
        GrokLaunchArgs.ReasoningEffortForModel(AgentModelLevel.Frontier, "grok-4.6").ShouldBe("xhigh");
    }
}

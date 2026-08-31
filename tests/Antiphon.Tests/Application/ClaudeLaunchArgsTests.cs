using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0289 — Claude's launch-time <c>--effort</c> flag, measured against claude 2.1.251 on
/// 2026-08-31. The CLI accepts low / medium / high / xhigh / max; Antiphon never emits max.
/// </summary>
[Category("Unit")]
public class ClaudeLaunchArgsTests
{
    private static readonly string[] ClaudeCliCatalog = ["low", "medium", "high", "xhigh", "max"];

    [Test]
    [Arguments(AgentModelLevel.Frontier, "xhigh")]
    [Arguments(AgentModelLevel.High, "high")]
    [Arguments(AgentModelLevel.Medium, "medium")]
    [Arguments(AgentModelLevel.Low, "low")]
    public void every_tier_names_its_own_effort(AgentModelLevel level, string expected)
    {
        ClaudeLaunchArgs.Effort(level).ShouldBe(expected);
        ClaudeCliCatalog.ShouldContain(expected);
    }

    [Test]
    public void the_effort_ladder_is_monotonic_with_the_tier_ladder()
    {
        var order = new[] { "low", "medium", "high", "xhigh", "max" };
        var depths = Enum.GetValues<AgentModelLevel>()
            .OrderBy(level => (int)level)
            .Select(level => Array.IndexOf(order, ClaudeLaunchArgs.Effort(level)))
            .ToList();

        depths.ShouldAllBe(d => d >= 0, "every effort must be one the Claude CLI accepts");
        depths.ShouldBe(depths.OrderByDescending(d => d).ToList());
    }

    [Test]
    public void every_emitted_effort_is_in_the_cli_catalog_and_never_max()
    {
        foreach (var level in Enum.GetValues<AgentModelLevel>())
        {
            var effort = ClaudeLaunchArgs.Effort(level);
            ClaudeCliCatalog.ShouldContain(effort);
            effort.ShouldNotBe("max");
        }
    }

    [Test]
    public void the_flag_is_effort_as_its_own_argv_element()
    {
        ClaudeLaunchArgs.EffortFlag.ShouldBe("--effort");
        ClaudeLaunchArgs.EffortFlag.ShouldNotContain("=");
    }
}

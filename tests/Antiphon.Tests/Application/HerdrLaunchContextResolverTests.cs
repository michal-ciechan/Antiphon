using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0225: pane title is the agent's name, never the shared TUI profile id.</summary>
public class HerdrLaunchContextResolverTests
{
    [Test]
    public void PaneTitleFor_prefers_agent_Name_over_DefinitionName()
    {
        var agent = new Agent { Name = "PM-Orchestrator-Grok", Slug = "pm-orchestrator-grok" };
        var session = new AgentSession { DefinitionName = "grok-gkp-project" };

        HerdrLaunchContextResolver.PaneTitleFor(agent, session).ShouldBe("PM-Orchestrator-Grok");
    }

    [Test]
    public void PaneTitleFor_falls_back_to_Slug_when_Name_is_blank()
    {
        var agent = new Agent { Name = "  ", Slug = "pm-orchestrator-grok" };
        var session = new AgentSession { DefinitionName = "grok-gkp-project" };

        HerdrLaunchContextResolver.PaneTitleFor(agent, session).ShouldBe("pm-orchestrator-grok");
    }

    [Test]
    public void PaneTitleFor_falls_back_to_DefinitionName_when_agent_is_null()
    {
        var session = new AgentSession { DefinitionName = "grok-gkp-project" };

        HerdrLaunchContextResolver.PaneTitleFor(null, session).ShouldBe("grok-gkp-project");
    }

    [Test]
    public void PaneTitleFor_falls_back_to_agent_when_nothing_is_set()
    {
        HerdrLaunchContextResolver.PaneTitleFor(null, new AgentSession()).ShouldBe("agent");
        HerdrLaunchContextResolver.PaneTitleFor(
            new Agent { Name = "", Slug = "" },
            new AgentSession { DefinitionName = "  " }).ShouldBe("agent");
    }
}

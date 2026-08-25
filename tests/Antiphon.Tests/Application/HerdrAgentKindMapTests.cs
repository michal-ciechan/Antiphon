using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0187: every <see cref="AgentKind"/> is either mapped to a herdr kind string or listed
/// refused. A new enum member fails this test, not a live launch.
/// </summary>
public class HerdrAgentKindMapTests
{
    [Test]
    public void every_AgentKind_is_mapped_or_explicitly_refused()
    {
        var mapped = new HashSet<AgentKind>();
        var refused = HerdrAgentKindMap.Refused.ToHashSet();

        foreach (var kind in Enum.GetValues<AgentKind>())
        {
            var ok = HerdrAgentKindMap.TryMap(kind, out var herdrKind);
            if (refused.Contains(kind))
            {
                ok.ShouldBeFalse($"refused kind {kind} must not map");
                continue;
            }

            ok.ShouldBeTrue($"kind {kind} is neither mapped nor in HerdrAgentKindMap.Refused");
            HerdrAgentKinds.IsSupported(herdrKind).ShouldBeTrue();
            herdrKind.ShouldNotBeNull();
            mapped.Add(kind);
        }

        mapped.Intersect(refused).ShouldBeEmpty();
        mapped.Count.ShouldBe(Enum.GetValues<AgentKind>().Length - refused.Count);
    }

    [Test]
    [Arguments(AgentKind.ClaudeCode, HerdrAgentKinds.Claude)]
    [Arguments(AgentKind.Grok, HerdrAgentKinds.Grok)]
    [Arguments(AgentKind.Codex, HerdrAgentKinds.Codex)]
    public void mapped_kinds_round_trip_to_the_contracts_constants(AgentKind kind, string herdrKind)
    {
        HerdrAgentKindMap.TryMap(kind, out var mapped).ShouldBeTrue();
        mapped.ShouldBe(herdrKind);
        HerdrAgentKinds.Supported.ShouldContain(herdrKind);
        HerdrAgentKinds.IsSupported(herdrKind).ShouldBeTrue();
    }

    [Test]
    [Arguments(AgentKind.OpenCode)]
    [Arguments(AgentKind.Raw)]
    public void refused_kinds_do_not_map(AgentKind kind)
    {
        HerdrAgentKindMap.Refused.ShouldContain(kind);
        HerdrAgentKindMap.TryMap(kind, out _).ShouldBeFalse();
    }
}

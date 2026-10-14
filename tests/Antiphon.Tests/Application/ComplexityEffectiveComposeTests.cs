using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ComplexityEffectiveComposeTests
{
    [Test]
    public void A_required_pin_replaces_the_cell_and_sources_pin()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.Codex, AgentModelLevel.Frontier);
        var result = ComplexityEffective.Compose(Decision(pin),
            [Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, "fable")], "Plan/Hard", "role", Policy(), false);
        result.Source.ShouldBe("pin");
        result.Candidates.Select(c => (c.AgentKind, c.ModelLevel, c.Alias))
            .ShouldBe([(AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra")]);
        result.StagePin.ShouldNotBeNull().Id.ShouldBe(pin.Id);
        result.GovernsDefault.ShouldBeFalse();
        result.WithheldReason.ShouldBeNull();
    }

    [Test]
    public void A_preferred_pin_prepends_and_sources_pin_plus_chain()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Grok, AgentModelLevel.Frontier);
        // A missing row appends policy; an existing row deduplicates the repeated pin pair.
        foreach (var withRow in new[] { true, false })
        {
            var result = ComplexityEffective.Compose(Decision(pin), withRow
                ? [Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, "fable"),
                   Pair(AgentKind.Grok, AgentModelLevel.Frontier, "grok-4.7")]
                : [], "Plan/Medium", withRow ? "any" : "none", Policy(), true);
            result.Source.ShouldBe("pin+chain");
            result.Candidates.Select(c => (c.AgentKind, c.ModelLevel, c.Alias))
                .ShouldBe([(AgentKind.Grok, AgentModelLevel.Frontier, "grok-4.7"),
                    (AgentKind.ClaudeCode, AgentModelLevel.Frontier, "fable")]);
            result.StagePin.ShouldNotBeNull().Id.ShouldBe(pin.Id);
            result.WithheldReason.ShouldBeNull();
        }
    }

    [Test]
    public void No_pin_and_no_cell_falls_back_to_role_policy()
    {
        var policy = new PolicyResolution(AgentKind.ClaudeCode, AgentModelLevel.High, "opus", null);
        var result = ComplexityEffective.Compose(RoutingPinService.Decision.None, [], "Plan/Medium", "none", policy, true);
        result.Source.ShouldBe("rolePolicy");
        result.Candidates.Select(c => (c.AgentKind, c.ModelLevel, c.Alias))
            .ShouldBe([(AgentKind.ClaudeCode, AgentModelLevel.High, "opus")]);
        result.StagePin.ShouldBeNull();
        result.WithheldReason.ShouldBeNull();
        foreach (var source in new[] { "role", "any", "config" })
        {
            // The expected source is a literal input from the frozen whitelist, not another resolver.
            var own = ComplexityEffective.Compose(RoutingPinService.Decision.None,
                [Pair(AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra")], "Plan/Medium", source, policy, true);
            own.Source.ShouldBe(source);
            own.Candidates.Single().AgentKind.ShouldBe(AgentKind.Codex);
            own.StagePin.ShouldBeNull();
        }
        var forbid = Pin(RoutingPinStrength.Required, null, null);
        forbid.ForbiddenAliases = "opus";
        var constrained = ComplexityEffective.Compose(Decision(forbid),
            [Pair(AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra")], "Plan/Medium", "role", policy, true);
        constrained.Source.ShouldBe("role");
        constrained.StagePin.ShouldNotBeNull().Id.ShouldBe(forbid.Id);
        constrained.Candidates.Single().AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public void Governs_default_is_true_only_on_the_default_tier()
    {
        ComplexityEffective.Compose(RoutingPinService.Decision.None, [], "Plan/Medium", "none", Policy(), true)
            .GovernsDefault.ShouldBeTrue();
        ComplexityEffective.Compose(RoutingPinService.Decision.None, [], "Plan/Hard", "none", Policy(), false)
            .GovernsDefault.ShouldBeFalse();
    }

    [Test]
    [Arguments(AgentKind.Raw, AgentModelLevel.Frontier, "Plan policy names Raw/Frontier, which delegation cannot launch")]
    [Arguments(AgentKind.OpenCode, AgentModelLevel.Medium, "Plan policy names OpenCode/Medium, which delegation cannot launch")]
    [Arguments(null, (AgentModelLevel)999, "Plan policy names ClaudeCode/999, which delegation cannot launch")]
    public void An_unlaunchable_policy_is_withheld_with_its_reason(AgentKind? kind, AgentModelLevel level, string reason)
    {
        var settings = new DelegationSettings();
        settings.RolePolicy["Plan"] = new DelegationSettings.RolePolicyEntry { Kind = kind, Level = level };
        var policy = RolePolicyReadService.PolicyTarget(settings, AgentTaskRole.Plan);
        policy.Kind.ShouldBeNull();
        policy.Level.ShouldBeNull();
        policy.Alias.ShouldBeNull();
        policy.WithheldReason.ShouldBe(reason);
        var result = ComplexityEffective.Compose(RoutingPinService.Decision.None, [], "Plan/Medium", "none", policy, true);
        result.Source.ShouldBe("rolePolicy");
        result.Candidates.ShouldBeEmpty();
        result.WithheldReason.ShouldBe(reason);
        var shipped = RolePolicyReadService.PolicyTarget(new DelegationSettings(), AgentTaskRole.Plan);
        shipped.Kind.ShouldBe(AgentKind.ClaudeCode);
        shipped.Level.ShouldBe(AgentModelLevel.Frontier);
        shipped.Alias.ShouldBe("fable");
        shipped.WithheldReason.ShouldBeNull();
    }

    [Test]
    [Arguments("with-row")]
    [Arguments("no-row")]
    public void An_agent_only_stage_pin_withholds_the_default_target_and_sources_agent(string row)
    {
        foreach (var strength in new[] { RoutingPinStrength.Required, RoutingPinStrength.Preferred })
        {
            var pin = Pin(strength, null, null);
            pin.AgentId = Guid.NewGuid();
            var result = ComplexityEffective.Compose(Decision(pin), row == "with-row"
                ? [Pair(AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra")] : [],
                "Plan/Medium", row == "with-row" ? "role" : "none", Policy(), true, "astra-reviewer (Codex)");
            result.Source.ShouldBe("agent");
            result.Candidates.ShouldBeEmpty();
            result.StagePin.ShouldNotBeNull().Id.ShouldBe(pin.Id);
            result.GovernsDefault.ShouldBeFalse();
            result.WithheldReason.ShouldBe("stage pin binds standing agent astra-reviewer (Codex); an ordinary dispatch runs on that agent and does not walk this row");
        }
    }

    private static PolicyResolution Policy() => new(AgentKind.ClaudeCode, AgentModelLevel.Frontier, "fable", null);
    private static RoutingCandidates.Candidate Pair(AgentKind kind, AgentModelLevel level, string alias) =>
        new(kind, level, alias, RoutingCandidates.OriginChain);
    private static RoutingPin Pin(RoutingPinStrength strength, AgentKind? kind, AgentModelLevel? level) => new()
    {
        Id = Guid.NewGuid(), Role = AgentTaskRole.Plan, Strength = strength,
        Provenance = RoutingPinProvenance.Human, AgentKind = kind, ModelLevel = level,
        Reason = "C097M compose", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };
    private static RoutingPinService.Decision Decision(RoutingPin pin) =>
        new(pin, pin, null, pin.AgentKind, pin.ModelLevel, pin.AgentId, null, null, false, pin.Candidates);
}

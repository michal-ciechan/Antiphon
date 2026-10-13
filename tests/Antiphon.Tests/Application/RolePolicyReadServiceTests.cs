using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0097 S1: <see cref="RolePolicyReadService.Compose"/> is pure — settings plus a pin list,
/// no DB. Expected aliases are literals (F-1 in
/// docs/superpowers/plans/2026-10-13-card-0097-test-design.md).
/// </summary>
[Category("Unit")]
public sealed class RolePolicyReadServiceTests
{
    [Test]
    public void Compose_resolves_alias_from_kind()
    {
        var settings = new DelegationSettings();
        settings.RolePolicy["Docs"].Kind = AgentKind.Grok;
        settings.RolePolicy["Commit"].Kind = AgentKind.Codex;
        settings.RolePolicy["Debug"].EscalateAfterMinutes = 7;

        var dto = RolePolicyReadService.Compose(settings, []);

        var docs = Row(dto, AgentTaskRole.Docs);
        docs.Kind.ShouldBe(AgentKind.Grok);
        docs.Alias.ShouldBe("grok-4.7");
        docs.Effective.AgentKind.ShouldBe(AgentKind.Grok);
        docs.Effective.Alias.ShouldBe("grok-4.7");
        Row(dto, AgentTaskRole.Commit).Alias.ShouldBe("gpt-6.1-sol");
        var code = Row(dto, AgentTaskRole.Code);
        code.Kind.ShouldBeNull();
        code.Alias.ShouldBe("fable");
        code.Effective.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        Row(dto, AgentTaskRole.Debug).EscalateAfterMinutes.ShouldBe(7);
    }

    [Test]
    public void Compose_lists_RoutableRoles_in_order_and_marks_Custom_unconfigured()
    {
        var settings = new DelegationSettings
        {
            DefaultLevel = AgentModelLevel.Medium,
            DefaultTimeoutMinutes = 90,
        };

        var dto = RolePolicyReadService.Compose(settings, []);

        dto.DefaultLevel.ShouldBe(AgentModelLevel.Medium);
        dto.DefaultTimeoutMinutes.ShouldBe(90);
        dto.Roles.Select(r => r.Role).ShouldBe(
        [
            AgentTaskRole.Investigate, AgentTaskRole.Plan, AgentTaskRole.TestDesign, AgentTaskRole.Code,
            AgentTaskRole.Mutation, AgentTaskRole.Review, AgentTaskRole.Debug, AgentTaskRole.Coverage,
            AgentTaskRole.Docs, AgentTaskRole.Commit, AgentTaskRole.Test, AgentTaskRole.Deploy,
            AgentTaskRole.Merge, AgentTaskRole.Custom,
        ]);
        var custom = Row(dto, AgentTaskRole.Custom);
        custom.Configured.ShouldBeFalse();
        custom.Level.ShouldBe(AgentModelLevel.Medium);
        custom.Alias.ShouldBe("sonnet");
        custom.TimeoutMinutes.ShouldBe(90);
        custom.EscalateTo.ShouldBeNull();
        custom.Kind.ShouldBeNull();
        var plan = Row(dto, AgentTaskRole.Plan);
        plan.Configured.ShouldBeTrue();
        plan.Level.ShouldBe(AgentModelLevel.Frontier);
        plan.TimeoutMinutes.ShouldBe(240);
    }

    [Test]
    public void Compose_effective_prefers_the_first_stage_pin_candidate_and_excludes_card_pins()
    {
        var planPin = Pin(
            AgentTaskRole.Plan,
            RoutingPinStrength.Preferred,
            cardId: null,
            Candidate(AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra"),
            Candidate(AgentKind.ClaudeCode, AgentModelLevel.High, "opus"));
        var codeCardPin = Pin(
            AgentTaskRole.Code,
            RoutingPinStrength.Required,
            cardId: Guid.NewGuid(),
            Candidate(AgentKind.Grok, AgentModelLevel.Frontier, "grok-4.7"));

        var dto = RolePolicyReadService.Compose(new DelegationSettings(), [planPin, codeCardPin]);

        var plan = Row(dto, AgentTaskRole.Plan);
        plan.StagePin.ShouldBe(planPin);
        plan.Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra", RoutingPinStrength.Preferred));
        var code = Row(dto, AgentTaskRole.Code);
        code.StagePin.ShouldBeNull();
        code.Effective.Source.ShouldBe("rolePolicy");
    }

    [Test]
    public void Compose_fills_a_missing_pin_level_from_the_policy()
    {
        var pin = Pin(
            AgentTaskRole.Docs,
            RoutingPinStrength.Preferred,
            cardId: null,
            Candidate(AgentKind.Grok, null, null));

        var dto = RolePolicyReadService.Compose(new DelegationSettings(), [pin]);

        Row(dto, AgentTaskRole.Docs).Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.Grok, AgentModelLevel.Medium, "grok-4.7", RoutingPinStrength.Preferred));
    }

    [Test]
    public void Compose_fills_a_missing_pin_kind_from_the_policy()
    {
        var pin = Pin(
            AgentTaskRole.Docs,
            RoutingPinStrength.Required,
            cardId: null,
            Candidate(null, AgentModelLevel.High, null));

        var claude = RolePolicyReadService.Compose(new DelegationSettings(), [pin]);

        Row(claude, AgentTaskRole.Docs).Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.ClaudeCode, AgentModelLevel.High, "opus", RoutingPinStrength.Required));

        var settings = new DelegationSettings();
        settings.RolePolicy["Docs"].Kind = AgentKind.Grok;
        var grok = RolePolicyReadService.Compose(settings, [pin]);

        Row(grok, AgentTaskRole.Docs).Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.Grok, AgentModelLevel.High, "grok-4.7", RoutingPinStrength.Required));
    }

    [Test]
    public void Compose_withholds_the_target_for_a_candidate_free_pin()
    {
        var pin = Pin(AgentTaskRole.Docs, RoutingPinStrength.Required, cardId: null);

        var dto = RolePolicyReadService.Compose(new DelegationSettings(), [pin]);

        var docs = Row(dto, AgentTaskRole.Docs);
        docs.StagePin.ShouldBe(pin);
        docs.Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", null, null, null, RoutingPinStrength.Required));
    }

    private static RolePolicyRowDto Row(RolePolicyDto dto, AgentTaskRole role) =>
        dto.Roles.Single(r => r.Role == role);

    private static RoutingPinCandidateDto Candidate(AgentKind? kind, AgentModelLevel? level, string? alias) =>
        new(kind, level, alias, true, null);

    private static RoutingPinDto Pin(
        AgentTaskRole role,
        RoutingPinStrength strength,
        Guid? cardId,
        params RoutingPinCandidateDto[] candidates)
    {
        var head = candidates.Length > 0 ? candidates[0] : null;
        var now = new DateTime(2026, 10, 13, 16, 47, 0, DateTimeKind.Utc);
        return new RoutingPinDto(
            Guid.NewGuid(),
            cardId,
            cardId is null ? null : "CARD-9097",
            role,
            RoutingPinProvenance.Human,
            strength,
            head?.AgentKind,
            head?.ModelLevel,
            head?.Alias,
            null,
            [],
            null,
            null,
            "C097 compose",
            null,
            now,
            now,
            candidates,
            candidates.Length);
    }
}

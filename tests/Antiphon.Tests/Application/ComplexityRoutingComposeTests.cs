using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S1: <see cref="RoutingCandidates.Compose"/> is pure — no DB, no availability.
/// Required-pin-wins / Preferred-pin-prepends / empty chain live here so CARD-0322 can reuse
/// the same composer as a second list source.
/// </summary>
[Category("Unit")]
public sealed class ComplexityRoutingComposeTests
{
    [Test]
    public void An_empty_chain_with_no_pin_is_empty_and_not_walked()
    {
        var list = RoutingCandidates.Compose(
            RoutingPinService.Decision.None,
            [],
            "Hard",
            null,
            null,
            Resolve);

        list.Candidates.ShouldBeEmpty();
        list.Walked.ShouldBeFalse();
        list.Source.ShouldBe("chain:Hard");
    }

    [Test]
    public void A_chain_alone_keeps_order_and_origin_chain()
    {
        var chain = new[]
        {
            Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
            Pair(AgentKind.Grok, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(
            RoutingPinService.Decision.None, chain, "Hard", null, null, Resolve);

        list.Candidates.Select(c => c.Alias).ShouldBe(["fable", "grok-4.6"]);
        list.Origins.ShouldBe([RoutingCandidates.OriginChain, RoutingCandidates.OriginChain]);
        list.Walked.ShouldBeTrue();
        list.Source.ShouldBe("chain:Hard");
    }

    [Test]
    public void A_required_pin_naming_kind_level_is_the_only_candidate()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.Codex, AgentModelLevel.Frontier, cardId: Guid.NewGuid());
        var chain = new[]
        {
            Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
            Pair(AgentKind.Grok, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(Decision(pin, "CARD-0301"), chain, "Hard", null, null, Resolve);

        list.Candidates.ShouldHaveSingleItem();
        list.Candidates[0].Alias.ShouldBe("gpt-6-astra");
        list.Candidates[0].Origin.ShouldBe(RoutingCandidates.OriginPin);
        list.Walked.ShouldBeFalse();
        list.Source.ShouldBe("pin:CARD-0301 Plan");
    }

    [Test]
    public void A_preferred_pin_is_prepended_then_the_chain_deduped()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Grok, AgentModelLevel.Frontier);
        var chain = new[]
        {
            Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
            Pair(AgentKind.Grok, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
            Pair(AgentKind.Codex, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(Decision(pin), chain, "Hard", null, null, Resolve);

        list.Candidates.Select(c => (c.Alias, c.Origin)).ShouldBe([
            ("grok-4.6", RoutingCandidates.OriginPin),
            ("fable", RoutingCandidates.OriginChain),
            ("gpt-6-astra", RoutingCandidates.OriginChain),
        ]);
        list.Walked.ShouldBeTrue();
        list.Source.ShouldStartWith("pin+chain:");
        list.Source.ShouldContain("Hard");
    }

    [Test]
    public void A_required_pin_without_kind_or_level_does_not_bypass_the_chain()
    {
        var pin = Pin(RoutingPinStrength.Required, kind: null, level: null);
        pin.ForbiddenAliases = "fable";
        var chain = new[]
        {
            Pair(AgentKind.Grok, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(Decision(pin), chain, "Easy", null, null, Resolve);

        list.Candidates.ShouldHaveSingleItem();
        list.Candidates[0].Origin.ShouldBe(RoutingCandidates.OriginChain);
        list.Source.ShouldBe("chain:Easy");
    }

    [Test]
    public void Explicit_request_kind_filters_the_chain_and_does_not_rewrite_pairs()
    {
        var chain = new[]
        {
            Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
            Pair(AgentKind.ClaudeCode, AgentModelLevel.High, RoutingCandidates.OriginChain),
            Pair(AgentKind.Grok, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(
            RoutingPinService.Decision.None, chain, "Hard", AgentKind.ClaudeCode, null, Resolve);

        list.Candidates.Select(c => c.Alias).ShouldBe(["fable", "opus"]);
        list.Walked.ShouldBeTrue();
    }

    [Test]
    public void A_preferred_pin_with_no_chain_appends_the_role_policy_candidate()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Codex, AgentModelLevel.Frontier);

        var list = RoutingCandidates.Compose(
            Decision(pin),
            chain: null,
            chainLabel: null,
            requestKind: null,
            requestLevel: null,
            Resolve);

        list.Candidates.Select(c => (c.Alias, c.Origin)).ShouldBe([
            ("gpt-6-astra", RoutingCandidates.OriginPin),
            ("opus", RoutingCandidates.OriginRolePolicy),
        ]);
        list.Walked.ShouldBeTrue();
        list.Source.ShouldStartWith("pin:");
    }

    [Test]
    public void Pin_plus_chain_source_strips_a_leading_pin_role_from_the_chain_label()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Grok, AgentModelLevel.Frontier, cardId: Guid.NewGuid());
        var chain = new[]
        {
            Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(
            Decision(pin, "CARD-0301"), chain, "Plan/Hard", null, null, Resolve);

        list.Source.ShouldBe("pin+chain:CARD-0301 Plan/Hard");
    }

    [Test]
    public void A_required_pin_list_is_the_whole_set_and_bypasses_the_chain()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.ClaudeCode, AgentModelLevel.Frontier, cardId: Guid.NewGuid());
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.High),
            new RoutingCandidate(AgentKind.Grok, AgentModelLevel.Frontier),
        ]);
        var chain = new[]
        {
            Pair(AgentKind.Codex, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(Decision(pin, "CARD-0301"), chain, "Hard", null, null, Resolve);

        list.Candidates.Select(c => (c.Alias, c.Origin)).ShouldBe([
            ("fable", RoutingCandidates.OriginPin),
            ("opus", RoutingCandidates.OriginPin),
            ("grok-4.6", RoutingCandidates.OriginPin),
        ]);
        list.Walked.ShouldBeTrue();
        list.Source.ShouldBe("pin:CARD-0301 Plan");
    }

    [Test]
    public void A_preferred_pin_list_then_the_chain_without_role_policy()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Codex, AgentModelLevel.Frontier);
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.Codex, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.Grok, AgentModelLevel.Frontier),
        ]);
        var chain = new[]
        {
            Pair(AgentKind.ClaudeCode, AgentModelLevel.Frontier, RoutingCandidates.OriginChain),
            Pair(AgentKind.ClaudeCode, AgentModelLevel.High, RoutingCandidates.OriginChain),
        };

        var list = RoutingCandidates.Compose(Decision(pin), chain, "Hard", null, null, Resolve);

        list.Candidates.Select(c => (c.Alias, c.Origin)).ShouldBe([
            ("gpt-6-astra", RoutingCandidates.OriginPin),
            ("grok-4.6", RoutingCandidates.OriginPin),
            ("fable", RoutingCandidates.OriginChain),
            ("opus", RoutingCandidates.OriginChain),
        ]);
        list.Walked.ShouldBeTrue();
        list.Source.ShouldStartWith("pin+chain:");
    }

    [Test]
    public void A_preferred_pin_list_without_a_chain_appends_role_policy_once()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Codex, AgentModelLevel.Frontier);
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.Codex, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.Grok, AgentModelLevel.Frontier),
        ]);

        var list = RoutingCandidates.Compose(
            Decision(pin), chain: null, chainLabel: null, null, null, Resolve);

        list.Candidates.Select(c => (c.Alias, c.Origin)).ShouldBe([
            ("gpt-6-astra", RoutingCandidates.OriginPin),
            ("grok-4.6", RoutingCandidates.OriginPin),
            ("opus", RoutingCandidates.OriginRolePolicy),
        ]);
        list.Walked.ShouldBeTrue();
    }

    [Test]
    public void Explicit_kind_narrows_the_pin_list_and_does_not_rewrite_pairs()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.High),
            new RoutingCandidate(AgentKind.Grok, AgentModelLevel.Frontier),
        ]);

        var list = RoutingCandidates.Compose(
            Decision(pin), chain: null, chainLabel: null, AgentKind.ClaudeCode, null, Resolve);

        list.Candidates.Select(c => c.Alias).ShouldBe(["fable", "opus"]);
        list.Walked.ShouldBeTrue();
    }

    [Test]
    public void Explicit_kind_and_level_against_a_list_is_one_candidate_and_not_walked()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.High),
        ]);

        var list = RoutingCandidates.Compose(
            Decision(pin),
            chain: null,
            chainLabel: null,
            AgentKind.ClaudeCode,
            AgentModelLevel.High,
            Resolve);

        list.Candidates.ShouldHaveSingleItem();
        list.Candidates[0].Alias.ShouldBe("opus");
        list.Walked.ShouldBeFalse();
    }

    [Test]
    public void Explicit_kind_only_narrows_to_a_single_non_head_survivor_and_is_not_walked()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.Grok, AgentModelLevel.Frontier);
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.Grok, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.High),
        ]);

        var list = RoutingCandidates.Compose(
            Decision(pin), chain: null, chainLabel: null, AgentKind.ClaudeCode, null, Resolve);

        list.Candidates.ShouldHaveSingleItem();
        list.Candidates[0].Kind.ShouldBe(AgentKind.ClaudeCode);
        list.Candidates[0].Level.ShouldBe(AgentModelLevel.High);
        list.Candidates[0].Alias.ShouldBe("opus");
        list.Walked.ShouldBeFalse();
    }

    [Test]
    public void Explicit_level_only_narrows_to_a_single_non_head_survivor_and_is_not_walked()
    {
        var pin = Pin(RoutingPinStrength.Required, AgentKind.Grok, AgentModelLevel.Frontier);
        pin.SetCandidates(
        [
            new RoutingCandidate(AgentKind.Grok, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.High),
        ]);

        var list = RoutingCandidates.Compose(
            Decision(pin), chain: null, chainLabel: null, null, AgentModelLevel.High, Resolve);

        list.Candidates.ShouldHaveSingleItem();
        list.Candidates[0].Kind.ShouldBe(AgentKind.ClaudeCode);
        list.Candidates[0].Level.ShouldBe(AgentModelLevel.High);
        list.Candidates[0].Alias.ShouldBe("opus");
        list.Walked.ShouldBeFalse();
    }

    [Test]
    public void Passing_an_empty_chain_does_not_append_role_policy()
    {
        var pin = Pin(RoutingPinStrength.Preferred, AgentKind.Codex, AgentModelLevel.Frontier);

        var list = RoutingCandidates.Compose(Decision(pin), [], "Hard", null, null, Resolve);

        list.Candidates.ShouldHaveSingleItem();
        list.Candidates[0].Origin.ShouldBe(RoutingCandidates.OriginPin);
        list.Walked.ShouldBeFalse();
    }

    private static RoutingCandidates.Candidate Resolve(AgentKind? kind, AgentModelLevel? level)
    {
        var k = kind ?? AgentKind.ClaudeCode;
        var l = level ?? AgentModelLevel.High;
        return new RoutingCandidates.Candidate(
            k, l, ModelLevelAliases.For(k, l), RoutingCandidates.OriginRolePolicy);
    }

    private static RoutingCandidates.Candidate Pair(
        AgentKind kind, AgentModelLevel level, string origin) =>
        new(kind, level, ModelLevelAliases.For(kind, level), origin);

    private static RoutingPin Pin(
        RoutingPinStrength strength, AgentKind? kind, AgentModelLevel? level, Guid? cardId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Strength = strength,
            Provenance = RoutingPinProvenance.Human,
            AgentKind = kind,
            ModelLevel = level,
            CardId = cardId,
            Reason = "test pin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    private static RoutingPinService.Decision Decision(RoutingPin pin, string? identifier = null) =>
        new(pin, pin, identifier, pin.AgentKind, pin.ModelLevel, null, null, null, false, pin.Candidates);
}

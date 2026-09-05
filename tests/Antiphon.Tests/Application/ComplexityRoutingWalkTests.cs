using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S1 walker: filter order (kind clamp → forbid → held → quota), first survivor,
/// Required single, Preferred prepend, empty → Chosen null with every reason filled.
/// </summary>
[Category("Integration")]
public class ComplexityRoutingWalkTests
{
    [Test]
    public async Task Walk_picks_the_first_survivor_and_records_held_skips()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", manual: true, until: DateTime.UtcNow.AddDays(1));

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen.ShouldNotBeNull();
        walk.Chosen!.Alias.ShouldBe("opus");
        walk.Outcomes[0].Outcome.ShouldBe("skipped");
        walk.Outcomes[0].Reason.ShouldNotBeNull();
        walk.Outcomes[0].Reason!.ShouldContain("held until");
        walk.Outcomes[0].Reason.ShouldContain("manual");
        walk.Outcomes[1].Outcome.ShouldBe("chosen");
        walk.ChainSource.ShouldBe("pin");
        walk.Source.ShouldBe("chain:Hard");
    }

    [Test]
    public async Task Walk_skips_non_delegatable_and_orchestrator_non_claude()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard,
            (AgentKind.Grok, AgentModelLevel.Frontier),
            (AgentKind.Codex, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier));

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Orchestrator, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen.ShouldNotBeNull();
        walk.Chosen!.Kind.ShouldBe(AgentKind.ClaudeCode);
        walk.Outcomes[0].Reason.ShouldBe("not a delegate kind");
        walk.Outcomes[1].Reason.ShouldBe("not a delegate kind");
        walk.Outcomes[2].Outcome.ShouldBe("chosen");
    }

    [Test]
    public async Task Walk_skips_a_stage_forbidden_alias_instead_of_throwing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        var stage = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Preferred,
            ForbiddenAliases = "fable",
            Reason = "no fable for Plan",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var pinDecision = new RoutingPinService.Decision(
            null, stage, null, null, null, null, null, null, false);

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            pinDecision, null, null, false, CancellationToken.None);

        walk.Chosen!.Alias.ShouldBe("grok-4.6");
        walk.Outcomes[0].Reason.ShouldBe("forbidden by stage pin");
    }

    [Test]
    public async Task A_human_card_pin_is_exempt_from_the_stage_forbid()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        var cardId = Guid.NewGuid();
        var cardPin = new RoutingPin
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Preferred,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Reason = "CARD-0301 stays on fable",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var stage = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Preferred,
            ForbiddenAliases = "fable",
            Reason = "no fable",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var pinDecision = new RoutingPinService.Decision(
            cardPin, stage, "CARD-0301", AgentKind.ClaudeCode, AgentModelLevel.Frontier,
            null, null, null, false);

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            pinDecision, cardId, null, false, CancellationToken.None);

        walk.Chosen!.Alias.ShouldBe("fable");
        walk.Outcomes[0].Outcome.ShouldBe("chosen");
    }

    [Test]
    public async Task A_required_pin_is_the_only_candidate_even_when_the_chain_has_more()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        var pin = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.Frontier,
            Reason = "Plan is Sol, full stop",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var pinDecision = new RoutingPinService.Decision(
            pin, null, null, AgentKind.Codex, AgentModelLevel.Frontier, null, null, null, false);

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            pinDecision, null, null, false, CancellationToken.None);

        walk.Chosen!.Alias.ShouldBe("gpt-6-astra");
        walk.Outcomes.ShouldHaveSingleItem();
        walk.Walked.ShouldBeFalse();
        walk.Source.ShouldStartWith("pin:");
    }

    [Test]
    public async Task All_held_fills_every_reason_and_Chosen_is_null()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", manual: true, until: null);
        await SeedHoldAsync(db, AgentKind.Grok, "grok-4.6", manual: true, until: null);

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen.ShouldBeNull();
        walk.Outcomes.Count.ShouldBe(2);
        walk.Outcomes.ShouldAllBe(o => o.Outcome == "skipped" && o.Reason != null);
        walk.ExhaustedSentence().ShouldStartWith(ComplexityRoutingService.RoutingExhaustedPrefix);
        walk.ExhaustedSentence().ShouldContain("fable");
        walk.ExhaustedSentence().ShouldContain("grok-4.6");
    }

    [Test]
    public async Task Config_default_is_used_when_no_row_exists()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var settings = new DelegationSettings
        {
            ComplexityChains =
            {
                ["Medium"] =
                [
                    new DelegationSettings.ComplexityCandidateSettings
                    {
                        Kind = AgentKind.Codex,
                        Level = AgentModelLevel.High,
                    },
                    new DelegationSettings.ComplexityCandidateSettings
                    {
                        Kind = AgentKind.Grok,
                        Level = AgentModelLevel.Frontier,
                    },
                ],
            },
        };

        var walk = await Routing(db, settings).WalkAsync(
            TaskComplexity.Medium, AgentTaskKind.Worker, AgentTaskRole.Code,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen!.Alias.ShouldBe("gpt-5.6-terra");
        walk.ChainSource.ShouldBe("config");
        walk.ChainProvenance.ShouldBe(RoutingPinProvenance.Auto);
        walk.Walked.ShouldBeTrue();
    }

    [Test]
    public async Task Empty_config_and_no_row_yields_Chosen_null()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen.ShouldBeNull();
        walk.Outcomes.ShouldBeEmpty();
        walk.ChainSource.ShouldBe("config");
        walk.ExhaustedSentence().ShouldContain("empty");
    }

    private static async Task SeedChainAsync(
        AppDbContext db,
        TaskComplexity complexity,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Complexity = complexity,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                pairs.Select(p => new ComplexityCandidatePair(p.Kind, p.Level)).ToList()),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test chain",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedHoldAsync(
        AppDbContext db, AgentKind kind, string alias, bool manual, DateTime? until)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = manual ? ModelAvailabilitySource.Manual : ModelAvailabilitySource.AutoDetected,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = manual ? "manual hold" : "auto hold",
        });
        await db.SaveChangesAsync();
    }

    private static ComplexityRoutingService Routing(AppDbContext db, DelegationSettings? settings = null)
    {
        settings ??= new DelegationSettings();
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        return new ComplexityRoutingService(
            db, Options.Create(settings), TimeProvider.System, availability);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}

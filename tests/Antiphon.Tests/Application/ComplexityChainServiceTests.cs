using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S1 writer: one active chain per complexity, Human as overwrite protection,
/// config default when no row, lazy NotAfter.
/// </summary>
[Category("Integration")]
public class ComplexityChainServiceTests
{
    [Test]
    public async Task A_human_chain_cannot_be_overwritten_by_an_auto_write()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(
            TaskComplexity.Hard,
            Put(Human: true, Reason: "operator: plan-grade work") with
            {
                Candidates =
                [
                    new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                ],
            },
            null,
            CancellationToken.None);

        var refusal = await Should.ThrowAsync<ConflictException>(() => service.UpsertAsync(
            TaskComplexity.Hard,
            Put(Human: false, Reason: "auto sweep"),
            null,
            CancellationToken.None));

        refusal.Code.ShouldBe("complexity_chain_human");
        refusal.StatusCode.ShouldBe(409);
        refusal.Message.ShouldContain("plan-grade work");
        var stored = await db.ComplexityChains.AsNoTracking().SingleAsync(c => c.ClearedAt == null);
        stored.Provenance.ShouldBe(RoutingPinProvenance.Human);
        stored.ParseCandidates()[0].AgentKind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task A_human_write_replaces_an_auto_chain_in_place()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(TaskComplexity.Medium, Put(Human: false), null, CancellationToken.None);
        var autoId = (await db.ComplexityChains.SingleAsync(c => c.ClearedAt == null)).Id;

        var human = await service.UpsertAsync(
            TaskComplexity.Medium,
            Put(Human: true, Reason: "operator: execute on grok") with
            {
                Candidates = [new(AgentKind.Grok, AgentModelLevel.Frontier)],
            },
            null,
            CancellationToken.None);

        human.Provenance.ShouldBe(RoutingPinProvenance.Human);
        human.Candidates.ShouldHaveSingleItem();
        human.Candidates[0].AgentKind.ShouldBe(AgentKind.Grok);
        var live = await db.ComplexityChains.AsNoTracking().Where(c => c.ClearedAt == null).ToListAsync();
        live.ShouldHaveSingleItem();
        live[0].Id.ShouldBe(autoId);
    }

    [Test]
    public async Task Duplicate_and_empty_and_non_delegatable_candidates_are_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);

        var empty = await Should.ThrowAsync<ValidationException>(() =>
            service.UpsertAsync(TaskComplexity.Easy, Put(Human: true) with { Candidates = [] }, null, CancellationToken.None));
        empty.StatusCode.ShouldBe(422);

        var dup = await Should.ThrowAsync<ValidationException>(() =>
            service.UpsertAsync(
                TaskComplexity.Easy,
                Put(Human: true) with
                {
                    Candidates =
                    [
                        new(AgentKind.Grok, AgentModelLevel.Frontier),
                        new(AgentKind.Grok, AgentModelLevel.Frontier),
                    ],
                },
                null,
                CancellationToken.None));
        dup.Errors.ShouldContainKey("Candidates");
        dup.Message.ShouldBe("One or more validation errors occurred.");

        var kind = await Should.ThrowAsync<ValidationException>(() =>
            service.UpsertAsync(
                TaskComplexity.Easy,
                Put(Human: true) with { Candidates = [new(AgentKind.OpenCode, AgentModelLevel.High)] },
                null,
                CancellationToken.None));
        kind.Errors["Candidates"][0].ShouldContain("not a delegate kind");
    }

    [Test]
    public async Task A_notAfter_in_the_past_is_refused()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() => Service(db).UpsertAsync(
            TaskComplexity.Hard,
            Put(Human: true) with { NotAfter = DateTimeOffset.UtcNow.AddHours(-1) },
            null,
            CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("NotAfter");
    }

    [Test]
    public async Task A_notAfter_that_has_passed_clears_the_row_on_the_next_read()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                [new ComplexityCandidatePair(AgentKind.Grok, AgentModelLevel.Frontier)]),
            Provenance = RoutingPinProvenance.Auto,
            Reason = "temporary",
            NotAfter = DateTime.UtcNow.AddSeconds(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var dto = await Service(db).GetAsync(TaskComplexity.Hard, CancellationToken.None);

        dto.Source.ShouldBe("config");
        dto.Candidates.ShouldBeEmpty();
        (await db.ComplexityChains.AsNoTracking().SingleAsync()).ClearedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Get_uses_the_config_default_when_no_row_exists()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var settings = new DelegationSettings
        {
            ComplexityChains =
            {
                ["Easy"] =
                [
                    new DelegationSettings.ComplexityCandidateSettings
                    {
                        Kind = AgentKind.Grok,
                        Level = AgentModelLevel.Frontier,
                    },
                ],
            },
        };

        var dto = await Service(db, settings).GetAsync(TaskComplexity.Easy, CancellationToken.None);

        dto.Source.ShouldBe("config");
        dto.Provenance.ShouldBe(RoutingPinProvenance.Auto);
        dto.Candidates.ShouldHaveSingleItem();
        dto.Candidates[0].Alias.ShouldBe("grok-4.6");
        dto.Candidates[0].AvailableNow.ShouldBeTrue();
    }

    [Test]
    public async Task Clearing_is_idempotent_and_returns_the_config_default()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(TaskComplexity.Hard, Put(Human: true), null, CancellationToken.None);

        await service.ClearAsync(TaskComplexity.Hard, CancellationToken.None);
        await service.ClearAsync(TaskComplexity.Hard, CancellationToken.None);

        (await db.ComplexityChains.AsNoTracking().SingleAsync()).ClearedAt.ShouldNotBeNull();
        var dto = await service.GetAsync(TaskComplexity.Hard, CancellationToken.None);
        dto.Source.ShouldBe("config");
        dto.Candidates.ShouldBeEmpty();
    }

    [Test]
    public async Task List_always_returns_three_tiers()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await Service(db).UpsertAsync(TaskComplexity.Hard, Put(Human: true), null, CancellationToken.None);

        var list = await Service(db).ListAsync(CancellationToken.None);

        list.Chains.Select(c => c.Complexity).ShouldBe(
            [TaskComplexity.Hard, TaskComplexity.Medium, TaskComplexity.Easy]);
        list.Chains.Single(c => c.Complexity == TaskComplexity.Hard).Source.ShouldBe("pin");
        list.Chains.Single(c => c.Complexity == TaskComplexity.Medium).Source.ShouldBe("config");
    }

    [Test]
    public async Task Get_marks_a_held_candidate_unavailable_now()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = DateTime.UtcNow.AddDays(1),
            HitAt = DateTime.UtcNow,
            Reason = "manual hold",
        });
        await db.SaveChangesAsync();
        await Service(db).UpsertAsync(
            TaskComplexity.Hard,
            Put(Human: true) with
            {
                Candidates =
                [
                    new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                ],
            },
            null,
            CancellationToken.None);

        var dto = await Service(db).GetAsync(TaskComplexity.Hard, CancellationToken.None);

        var fable = dto.Candidates.Single(c => c.Alias == "fable");
        fable.AvailableNow.ShouldBeFalse();
        fable.UnavailableReason.ShouldContain("held until");
        fable.UnavailableReason.ShouldContain("manual");
        dto.Candidates.Single(c => c.Alias == "grok-4.6").AvailableNow.ShouldBeTrue();
    }

    private static PutComplexityChainRequest Put(bool Human, string? Reason = null) =>
        new(
            [new ComplexityCandidateRequest(AgentKind.ClaudeCode, AgentModelLevel.High)],
            Human ? RoutingPinProvenance.Human : RoutingPinProvenance.Auto,
            Reason);

    private static ComplexityChainService Service(AppDbContext db, DelegationSettings? settings = null)
    {
        settings ??= new DelegationSettings();
        var options = Options.Create(settings);
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        var routing = new ComplexityRoutingService(
            db, options, TimeProvider.System, availability);
        return new ComplexityChainService(
            db, TimeProvider.System, routing, options, NullLogger<ComplexityChainService>.Instance);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}

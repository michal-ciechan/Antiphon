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
/// CARD-0332 S1: (Role?, Complexity) keying, D3 whole-row fallback, D6 Auto-cell shadow of a
/// Human any-role row, role 422s, list/effective DTO shapes. Existing Complexity* tests stay
/// on the any-role overloads.
/// </summary>
[Category("Integration")]
public class ComplexityChainRoleTests
{
    [Test]
    public async Task A_null_role_row_resolves_for_Plan_Hard_as_chain_Hard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard, role: null,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Source.ShouldBe("chain:Hard");
        walk.ChainRole.ShouldBeNull();
        walk.Role.ShouldBe(AgentTaskRole.Plan);
        walk.CellLabel.ShouldBe("Hard");
        walk.Chosen!.Alias.ShouldBe("fable");
    }

    [Test]
    public async Task A_Plan_Hard_cell_wins_as_a_whole_row_over_any_role_Hard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard, role: null,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedChainAsync(db, TaskComplexity.Hard, AgentTaskRole.Plan,
            (AgentKind.Codex, AgentModelLevel.Frontier));

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Source.ShouldBe("chain:Plan/Hard");
        walk.ChainRole.ShouldBe(AgentTaskRole.Plan);
        walk.CellLabel.ShouldBe("Plan/Hard");
        walk.Chosen!.Alias.ShouldBe("gpt-6-astra");
        walk.Outcomes.ShouldHaveSingleItem();
    }

    [Test]
    public async Task Code_Hard_with_no_cell_reads_the_any_role_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard, role: null,
            (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedChainAsync(db, TaskComplexity.Hard, AgentTaskRole.Plan,
            (AgentKind.Codex, AgentModelLevel.Frontier));

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Code,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Source.ShouldBe("chain:Hard");
        walk.ChainRole.ShouldBeNull();
        walk.Chosen!.Alias.ShouldBe("grok-4.6");
    }

    [Test]
    public async Task No_cell_no_any_role_row_uses_config()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var settings = new DelegationSettings
        {
            ComplexityChains =
            {
                ["Hard"] =
                [
                    new DelegationSettings.ComplexityCandidateSettings
                    {
                        Kind = AgentKind.Grok,
                        Level = AgentModelLevel.Frontier,
                    },
                ],
            },
        };

        var walk = await Routing(db, settings).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen!.Alias.ShouldBe("grok-4.6");
        walk.ChainSource.ShouldBe("config");
        walk.ChainRole.ShouldBeNull();
        walk.Source.ShouldBe("chain:Hard");
    }

    [Test]
    public async Task Nothing_yields_Chosen_null_and_the_sentence_names_all_three()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Chosen.ShouldBeNull();
        walk.Outcomes.ShouldBeEmpty();
        walk.ExhaustedSentence().ShouldContain("Plan/Hard chain is empty");
        walk.ExhaustedSentence().ShouldContain("no Plan/Hard row");
        walk.ExhaustedSentence().ShouldContain("no any-role Hard row");
        walk.ExhaustedSentence().ShouldContain("no config default");
        walk.ExhaustedSentence().ShouldContain("set -Role Plan -Complexity Hard");
        walk.ExhaustedSentence().ShouldContain("set -Complexity Hard for every role");
    }

    [Test]
    public async Task Lazy_NotAfter_on_a_cell_falls_back_to_the_any_role_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard, role: null,
            (AgentKind.Grok, AgentModelLevel.Frontier));
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Plan,
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                [new ComplexityCandidatePair(AgentKind.Codex, AgentModelLevel.Frontier)]),
            Provenance = RoutingPinProvenance.Human,
            Reason = "temporary Plan cell",
            NotAfter = DateTime.UtcNow.AddSeconds(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var walk = await Routing(db).WalkAsync(
            TaskComplexity.Hard, AgentTaskKind.Worker, AgentTaskRole.Plan,
            RoutingPinService.Decision.None, null, null, false, CancellationToken.None);

        walk.Source.ShouldBe("chain:Hard");
        walk.ChainRole.ShouldBeNull();
        walk.Chosen!.Alias.ShouldBe("grok-4.6");
        (await db.ComplexityChains.AsNoTracking()
            .SingleAsync(c => c.Role == AgentTaskRole.Plan)).ClearedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Human_any_role_plus_Auto_cell_PUT_is_409()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(
            role: null,
            TaskComplexity.Hard,
            Put(Human: true, Reason: "operator: any-role Hard"),
            null,
            CancellationToken.None);

        var refusal = await Should.ThrowAsync<ConflictException>(() => service.UpsertAsync(
            AgentTaskRole.Plan,
            TaskComplexity.Hard,
            Put(Human: false, Reason: "auto cell"),
            null,
            CancellationToken.None));

        refusal.Code.ShouldBe("complexity_chain_human");
        refusal.StatusCode.ShouldBe(409);
        refusal.Message.ShouldContain("any-role Hard");
        refusal.Message.ShouldContain("Write it as Human, or clear the any-role row");
        (await db.ComplexityChains.CountAsync(c => c.ClearedAt == null)).ShouldBe(1);
    }

    [Test]
    public async Task Human_cell_over_Human_any_role_is_allowed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(
            role: null, TaskComplexity.Hard, Put(Human: true, Reason: "any-role"), null, CancellationToken.None);

        var cell = await service.UpsertAsync(
            AgentTaskRole.Plan,
            TaskComplexity.Hard,
            Put(Human: true, Reason: "Plan cell") with
            {
                Candidates = [new(AgentKind.Grok, AgentModelLevel.Frontier)],
            },
            null,
            CancellationToken.None);

        cell.Role.ShouldBe(AgentTaskRole.Plan);
        cell.ResolvedFrom.ShouldBe("role");
        cell.Candidates.ShouldHaveSingleItem();
        cell.Candidates[0].AgentKind.ShouldBe(AgentKind.Grok);
        (await db.ComplexityChains.CountAsync(c => c.ClearedAt == null)).ShouldBe(2);
    }

    [Test]
    public async Task Check_Distill_Diagnose_are_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);

        foreach (var role in new[] { AgentTaskRole.Check, AgentTaskRole.Distill, AgentTaskRole.Diagnose })
        {
            var failure = await Should.ThrowAsync<ValidationException>(() =>
                service.UpsertAsync(role, TaskComplexity.Hard, Put(Human: true), null, CancellationToken.None));
            failure.StatusCode.ShouldBe(422);
            failure.Errors["role"][0].ShouldContain("seat-pinned roles are not routed by chains");
        }
    }

    [Test]
    public async Task Unknown_role_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                (AgentTaskRole)999, TaskComplexity.Hard, Put(Human: true), null, CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("role");
    }

    [Test]
    public async Task List_is_three_any_role_entries_then_cells()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(role: null, TaskComplexity.Hard, Put(Human: true), null, CancellationToken.None);
        await service.UpsertAsync(
            AgentTaskRole.Code, TaskComplexity.Easy, Put(Human: true, Reason: "Code/Easy"), null, CancellationToken.None);
        await service.UpsertAsync(
            AgentTaskRole.Plan, TaskComplexity.Hard, Put(Human: true, Reason: "Plan/Hard"), null, CancellationToken.None);

        var list = await service.ListAsync(CancellationToken.None);

        list.Chains.Count.ShouldBe(5);
        list.Chains.Take(3).Select(c => (c.Complexity, c.Role, c.ResolvedFrom)).ShouldBe([
            (TaskComplexity.Hard, (AgentTaskRole?)null, "any"),
            (TaskComplexity.Medium, null, "none"),
            (TaskComplexity.Easy, null, "none"),
        ]);
        list.Chains[3].Role.ShouldBe(AgentTaskRole.Plan);
        list.Chains[3].Complexity.ShouldBe(TaskComplexity.Hard);
        list.Chains[3].ResolvedFrom.ShouldBe("role");
        list.Chains[4].Role.ShouldBe(AgentTaskRole.Code);
        list.Chains[4].Complexity.ShouldBe(TaskComplexity.Easy);
        list.Roles.ShouldBe(ComplexityRoutingService.RoutableRoles);
        list.Complexities.ShouldBe(["Hard", "Medium", "Easy"]);
    }

    [Test]
    public async Task Get_role_Plan_resolvedFrom_matrix()
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
        var service = Service(db, settings);
        await service.UpsertAsync(
            AgentTaskRole.Plan, TaskComplexity.Hard, Put(Human: true, Reason: "own"), null, CancellationToken.None);
        await service.UpsertAsync(
            role: null, TaskComplexity.Medium, Put(Human: true, Reason: "any Medium"), null, CancellationToken.None);

        var list = await service.ListAsync(AgentTaskRole.Plan, CancellationToken.None);

        list.Chains.Select(c => (c.Complexity, c.Role, c.ResolvedFrom)).ShouldBe([
            (TaskComplexity.Hard, AgentTaskRole.Plan, "role"),
            (TaskComplexity.Medium, AgentTaskRole.Plan, "any"),
            (TaskComplexity.Easy, AgentTaskRole.Plan, "config"),
        ]);

        var empty = await Service(db).GetEffectiveAsync(AgentTaskRole.Debug, TaskComplexity.Hard, CancellationToken.None);
        empty.ResolvedFrom.ShouldBe("none");
        empty.Candidates.ShouldBeEmpty();
    }

    [Test]
    public async Task Two_any_role_Hard_rows_cannot_coexist()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedChainAsync(db, TaskComplexity.Hard, role: null,
            (AgentKind.Grok, AgentModelLevel.Frontier));

        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = null,
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                [new ComplexityCandidatePair(AgentKind.Codex, AgentModelLevel.Frontier)]),
            Provenance = RoutingPinProvenance.Auto,
            Reason = "duplicate any-role",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var failure = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        failure.InnerException.ShouldNotBeNull();
        failure.InnerException!.Message.ShouldContain("IX_ComplexityChains_Role_Complexity_Active");
    }

    [Test]
    public async Task Put_twice_on_the_same_cell_updates_in_place()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var service = Service(db);
        await service.UpsertAsync(
            AgentTaskRole.Plan, TaskComplexity.Hard, Put(Human: false), null, CancellationToken.None);
        var id = (await db.ComplexityChains.SingleAsync(c => c.ClearedAt == null)).Id;

        await service.UpsertAsync(
            AgentTaskRole.Plan,
            TaskComplexity.Hard,
            Put(Human: true, Reason: "replace") with
            {
                Candidates = [new(AgentKind.Grok, AgentModelLevel.Frontier)],
            },
            null,
            CancellationToken.None);

        var live = await db.ComplexityChains.AsNoTracking().Where(c => c.ClearedAt == null).ToListAsync();
        live.ShouldHaveSingleItem();
        live[0].Id.ShouldBe(id);
        live[0].Role.ShouldBe(AgentTaskRole.Plan);
        live[0].Provenance.ShouldBe(RoutingPinProvenance.Human);
    }

    private static async Task SeedChainAsync(
        AppDbContext db,
        TaskComplexity complexity,
        AgentTaskRole? role,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = role,
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

using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0305 S1, the writer half: one active pin per grain, and Human as overwrite protection
/// rather than a second key.
///
/// <para>Each test takes its own migrated schema. The stage-wide index is unique on ROLE alone, so
/// two tests writing a Plan stage pin against the shared container would collide on each other
/// rather than on anything they were asserting about.</para>
/// </summary>
[Category("Integration")]
public class RoutingPinServiceTests
{
    [Test]
    public async Task A_human_pin_cannot_be_overwritten_by_an_auto_write()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pins = Service(db);
        await pins.UpsertAsync(
            StagePin(AgentTaskRole.Plan) with
            {
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.Codex,
                ModelLevel = AgentModelLevel.Frontier,
                Reason = "operator: planning off fable",
            },
            null,
            CancellationToken.None);

        // The whole reason provenance is a column: "everything moves off fable" is an Auto-level
        // sweep and must not silently take a decision the operator made by hand.
        var refusal = await Should.ThrowAsync<ConflictException>(() => pins.UpsertAsync(
            StagePin(AgentTaskRole.Plan) with { AgentKind = AgentKind.Grok },
            null,
            CancellationToken.None));

        refusal.Code.ShouldBe("routing_pin_human");
        refusal.StatusCode.ShouldBe(409);
        refusal.Message.ShouldContain("planning off fable");
        var stored = await db.RoutingPins.AsNoTracking().SingleAsync(p => p.ClearedAt == null);
        stored.AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task A_human_write_replaces_an_auto_pin_in_place()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pins = Service(db);
        var auto = await pins.UpsertAsync(
            StagePin(AgentTaskRole.Code) with { AgentKind = AgentKind.ClaudeCode },
            null,
            CancellationToken.None);

        var human = await pins.UpsertAsync(
            StagePin(AgentTaskRole.Code) with
            {
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.Grok,
                ForbiddenAliases = ["fable"],
                Reason = "operator: execute on Grok",
            },
            null,
            CancellationToken.None);

        // Same row, not a second active one — the unique index is the contract, and a second row
        // would make "which pin applies" ambiguous.
        human.Id.ShouldBe(auto.Id);
        human.Provenance.ShouldBe(RoutingPinProvenance.Human);
        human.AgentKind.ShouldBe(AgentKind.Grok);
        human.ForbiddenAliases.ShouldBe(["fable"]);
        (await db.RoutingPins.CountAsync(p => p.ClearedAt == null)).ShouldBe(1);
    }

    [Test]
    public async Task A_card_pin_and_the_stage_pin_for_the_same_role_coexist()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0301");
        var pins = Service(db);

        await pins.UpsertAsync(
            StagePin(AgentTaskRole.Plan) with
            {
                Provenance = RoutingPinProvenance.Human,
                ForbiddenAliases = ["fable"],
            },
            null,
            CancellationToken.None);
        var cardPin = await pins.UpsertAsync(
            StagePin(AgentTaskRole.Plan) with
            {
                Card = "CARD-0301",
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
            },
            null,
            CancellationToken.None);

        cardPin.CardId.ShouldBe(card.Id);
        cardPin.CardIdentifier.ShouldBe("CARD-0301");
        cardPin.ModelAlias.ShouldBe("fable");
        (await db.RoutingPins.CountAsync(p => p.ClearedAt == null)).ShouldBe(2);
    }

    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Distill)]
    [Arguments(AgentTaskRole.Diagnose)]
    public async Task A_specialist_role_has_no_stage_to_pin(AgentTaskRole role)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() => Service(db).UpsertAsync(
            StagePin(role), null, CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("Role");
    }

    [Test]
    public async Task A_notBefore_in_the_past_is_refused()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() => Service(db).UpsertAsync(
            StagePin(AgentTaskRole.Plan) with { NotBefore = DateTimeOffset.UtcNow.AddHours(-1) },
            null,
            CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("NotBefore");
    }

    [Test]
    public async Task An_unknown_forbidden_alias_is_refused_rather_than_dropped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() => Service(db).UpsertAsync(
            StagePin(AgentTaskRole.Plan) with { ForbiddenAliases = ["claude-fable-5-turbo"] },
            null,
            CancellationToken.None));

        // Dropping it would leave a pin that reads as "forbids nothing" while the operator
        // believes fable is excluded.
        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("ForbiddenAliases");
    }

    [Test]
    public async Task A_TUI_spelling_of_an_alias_is_normalised()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var pin = await Service(db).UpsertAsync(
            StagePin(AgentTaskRole.Plan) with { ForbiddenAliases = ["Fable 5", "fable"] },
            null,
            CancellationToken.None);

        pin.ForbiddenAliases.ShouldBe(["fable"]);
    }

    [Test]
    public async Task A_notAfter_that_has_passed_clears_the_pin_on_the_next_read()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var id = Guid.NewGuid();
        db.RoutingPins.Add(new RoutingPin
        {
            Id = id,
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Auto,
            Strength = RoutingPinStrength.Preferred,
            AgentKind = AgentKind.Grok,
            NotAfter = DateTime.UtcNow.AddSeconds(-2),
            Reason = "temporary quota fallback",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var live = await Service(db).FindActiveAsync(null, AgentTaskRole.Plan, CancellationToken.None);

        live.ShouldBeNull();
        (await db.RoutingPins.AsNoTracking().SingleAsync(p => p.Id == id)).ClearedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Clearing_is_idempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pins = Service(db);
        var pin = await pins.UpsertAsync(StagePin(AgentTaskRole.Docs), null, CancellationToken.None);

        (await pins.ClearAsync(pin.Id, CancellationToken.None)).ShouldBeTrue();
        (await pins.ClearAsync(pin.Id, CancellationToken.None)).ShouldBeTrue();
        (await pins.ClearAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();

        // Cleared, not deleted: the row is the record of what was pinned and by whom.
        (await db.RoutingPins.AsNoTracking().SingleAsync(p => p.Id == pin.Id)).ClearedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_pool_delegate_cannot_be_pinned_to_a_stage()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "pool-delegate",
            Slug = "pool-delegate",
            WorkingDirectory = Path.GetTempPath(),
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var failure = await Should.ThrowAsync<ValidationException>(() => Service(db).UpsertAsync(
            StagePin(AgentTaskRole.Plan) with { AgentId = agentId }, null, CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("AgentId");
    }

    [Test]
    public async Task Get_for_a_card_also_returns_the_stage_pin_that_would_apply()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await SeedCardAsync(db, "CARD-0304");
        var pins = Service(db);
        await pins.UpsertAsync(StagePin(AgentTaskRole.Plan), null, CancellationToken.None);
        await pins.UpsertAsync(
            StagePin(AgentTaskRole.Code) with { Card = "CARD-0304", AgentKind = AgentKind.Grok },
            null,
            CancellationToken.None);

        var all = await pins.ListAsync("CARD-0304", null, CancellationToken.None);

        // The stage row is the answer to "what happens if this card has no pin of its own", so
        // hiding it would answer a different question than the one that was asked.
        all.Count.ShouldBe(2);
        all.ShouldContain(p => p.CardId == null && p.Role == AgentTaskRole.Plan);
        all.ShouldContain(p => p.CardIdentifier == "CARD-0304" && p.Role == AgentTaskRole.Code);
    }

    private static PutRoutingPinRequest StagePin(AgentTaskRole role) =>
        new(role, Reason: "test pin");

    private static RoutingPinService Service(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    internal static async Task<Card> SeedCardAsync(AppDbContext db, string identifier)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"pin-project-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/pin.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Pin board {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} routing pin test card",
            Description = "Routing pin test.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }
}

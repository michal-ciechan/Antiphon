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
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0322 S1: candidate-list write validation, JSON round-trip, GET availableNow, and the
/// add+backfill+drop migration. Existing RoutingPin* tests stay unedited.
/// </summary>
[Category("Integration")]
public sealed class RoutingPinCandidateTests
{
    [Test]
    public async Task A_three_candidate_put_stores_json_and_returns_the_head()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var dto = await Service(db).UpsertAsync(
            StagePin() with
            {
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                Candidates =
                [
                    new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                    new(AgentKind.ClaudeCode, AgentModelLevel.High),
                    new(AgentKind.Codex, AgentModelLevel.Frontier),
                ],
                Reason = "plan on fable, opus, then sol",
            },
            null,
            CancellationToken.None);

        dto.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        dto.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        dto.ModelAlias.ShouldBe("fable");
        dto.CandidateCount.ShouldBe(3);
        dto.Candidates.ShouldNotBeNull();
        dto.Candidates!.Select(c => (c.AgentKind, c.ModelLevel, c.Alias)).ShouldBe([
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier, "fable"),
            (AgentKind.ClaudeCode, AgentModelLevel.High, "opus"),
            (AgentKind.Codex, AgentModelLevel.Frontier, "gpt-5.6-sol"),
        ]);

        var stored = await db.RoutingPins.AsNoTracking().SingleAsync(p => p.ClearedAt == null);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        stored.Candidates.Count.ShouldBe(3);
        stored.Head!.Describe().ShouldBe("ClaudeCode/Frontier");
        stored.CandidatesJson.ShouldContain("\"agentKind\":\"ClaudeCode\"");
        stored.CandidatesJson.ShouldContain("\"modelLevel\":\"Frontier\"");
    }

    [Test]
    public async Task Empty_candidates_array_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                StagePin() with { Candidates = [] },
                null,
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors.ShouldContainKey("Candidates");
    }

    [Test]
    public async Task Nine_candidates_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        RoutingCandidateRequest[] nine =
        [
            new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            new(AgentKind.ClaudeCode, AgentModelLevel.High),
            new(AgentKind.ClaudeCode, AgentModelLevel.Medium),
            new(AgentKind.ClaudeCode, AgentModelLevel.Low),
            new(AgentKind.Grok, AgentModelLevel.Frontier),
            new(AgentKind.Grok, AgentModelLevel.High),
            new(AgentKind.Codex, AgentModelLevel.Frontier),
            new(AgentKind.Codex, AgentModelLevel.High),
            new(AgentKind.Codex, AgentModelLevel.Medium),
        ];

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(StagePin() with { Candidates = nine }, null, CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors["Candidates"][0].ShouldContain("at most 8");
    }

    [Test]
    public async Task Duplicate_candidates_are_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                StagePin() with
                {
                    Candidates =
                    [
                        new(AgentKind.Grok, AgentModelLevel.Frontier),
                        new(AgentKind.Grok, AgentModelLevel.Frontier),
                    ],
                },
                null,
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors["Candidates"][0].ShouldContain("Duplicate");
    }

    [Test]
    public async Task A_both_null_candidate_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                StagePin() with { Candidates = [new RoutingCandidateRequest()] },
                null,
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors["Candidates"][0].ShouldContain("kind or a level");
    }

    [Test]
    public async Task A_non_delegatable_kind_on_the_list_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                StagePin() with
                {
                    Candidates = [new(AgentKind.OpenCode, AgentModelLevel.Frontier)],
                },
                null,
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors["Candidates"][0].ShouldContain("OpenCode");
    }

    [Test]
    public async Task Shorthand_together_with_the_list_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                StagePin() with
                {
                    AgentKind = AgentKind.Grok,
                    Candidates = [new(AgentKind.ClaudeCode, AgentModelLevel.Frontier)],
                },
                null,
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors["Candidates"][0].ShouldContain("shorthand");
    }

    [Test]
    public async Task A_standing_agent_with_two_candidates_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "standing",
            Slug = "standing",
            WorkingDirectory = Path.GetTempPath(),
            Details = "Standing agent.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var failure = await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync(
                StagePin() with
                {
                    AgentId = agentId,
                    Candidates =
                    [
                        new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                        new(AgentKind.ClaudeCode, AgentModelLevel.High),
                    ],
                },
                null,
                CancellationToken.None));

        failure.StatusCode.ShouldBe(422);
        failure.Errors["Candidates"][0].ShouldContain("standing agent");
    }

    [Test]
    public async Task Get_shows_availableNow_false_with_the_hold_reason()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        await Service(db).UpsertAsync(
            StagePin() with
            {
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Reason = "plan on fable",
            },
            null,
            CancellationToken.None);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = DateTime.UtcNow.AddHours(4),
            HitAt = DateTime.UtcNow,
            Reason = "weekly cap",
        });
        await db.SaveChangesAsync();

        var listed = await Service(db, availability: true)
            .ListAsync(null, AgentTaskRole.Plan, CancellationToken.None);

        var pin = listed.ShouldHaveSingleItem();
        pin.CandidateCount.ShouldBe(1);
        pin.Candidates.ShouldNotBeNull();
        pin.Candidates![0].AvailableNow.ShouldBeFalse();
        pin.Candidates[0].UnavailableReason.ShouldNotBeNull();
        pin.Candidates[0].UnavailableReason.ShouldContain("held");
    }

    [Test]
    public async Task Backfill_round_trip_preserves_the_head_from_the_old_columns()
    {
        var databaseName = $"test_{Guid.NewGuid():N}";
        await using var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", maintenance))
            await create.ExecuteNonQueryAsync();

        IsolatedTestSchema? schema = null;
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;
            schema = new IsolatedTestSchema(databaseName, connectionString);
            var options = TestDbFixture.CreateDbContextOptions(connectionString);

            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync("20260905080000_AddProjectOrchestratorWorkspaceAcknowledgedAt");
            }

            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();
                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO "RoutingPins" (
                        "Id", "Role", "Provenance", "Strength", "AgentKind", "ModelLevel",
                        "Reason", "CreatedAt", "UpdatedAt")
                    VALUES
                        (@id1, 1, 1, 1, 1, 0, 'complete pair', NOW(), NOW()),
                        (@id2, 2, 1, 1, 4, NULL, 'kind only', NOW(), NOW()),
                        (@id3, 4, 1, 1, 4, 0, 'grok frontier', NOW(), NOW());
                    """,
                    conn);
                insert.Parameters.AddWithValue("id1", Guid.NewGuid());
                insert.Parameters.AddWithValue("id2", Guid.NewGuid());
                insert.Parameters.AddWithValue("id3", Guid.NewGuid());
                await insert.ExecuteNonQueryAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                var pins = await db.RoutingPins.AsNoTracking()
                    .OrderBy(p => p.Role)
                    .ToListAsync();
                pins.Count.ShouldBe(3);

                var plan = pins.Single(p => p.Role == AgentTaskRole.Plan);
                plan.Head.ShouldNotBeNull();
                plan.Head!.AgentKind.ShouldBe(AgentKind.ClaudeCode);
                plan.Head.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
                plan.AgentKind.ShouldBe(AgentKind.ClaudeCode);
                plan.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
                plan.Candidates.ShouldHaveSingleItem();

                var code = pins.Single(p => p.Role == AgentTaskRole.Code);
                code.Head.ShouldNotBeNull();
                code.Head!.AgentKind.ShouldBe(AgentKind.Grok);
                code.Head.ModelLevel.ShouldBeNull();
                code.AgentKind.ShouldBe(AgentKind.Grok);
                code.ModelLevel.ShouldBeNull();

                // AgentKind.Grok = 4, AgentModelLevel.Frontier = 0 — the mapping the review
                // verified by hand against AgentKind.cs / AgentModelLevel.cs ordinals.
                var debug = pins.Single(p => p.Role == AgentTaskRole.Debug);
                debug.Head.ShouldNotBeNull();
                debug.Head!.AgentKind.ShouldBe(AgentKind.Grok);
                debug.Head.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
                debug.AgentKind.ShouldBe(AgentKind.Grok);
                debug.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
                debug.Candidates.ShouldHaveSingleItem();
                debug.CandidatesJson.ShouldContain("Grok");
                debug.CandidatesJson.ShouldContain("Frontier");
            }
        }
        finally
        {
            if (schema is not null)
                await schema.DisposeAsync();
            else
                await TestDbFixture.DropClonedDatabaseAsync(databaseName);
        }
    }

    private static PutRoutingPinRequest StagePin() =>
        new(AgentTaskRole.Plan, Reason: "test pin");

    private static RoutingPinService Service(AppDbContext db, bool availability = false)
    {
        ComplexityRoutingService? routing = null;
        if (availability)
        {
            var settings = Options.Create(new DelegationSettings());
            var model = new ModelAvailability(
                db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
            routing = new ComplexityRoutingService(db, settings, TimeProvider.System, model);
        }

        return new RoutingPinService(
            db, TimeProvider.System, NullLogger<RoutingPinService>.Instance, routing);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}

[Category("Unit")]
public sealed class RoutingCandidateParseTests
{
    [Test]
    public void Parse_round_trips_enum_member_names_and_null_level()
    {
        var json = RoutingCandidate.Serialize(
        [
            new RoutingCandidate(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            new RoutingCandidate(AgentKind.Grok, null),
        ]);

        json.ShouldContain("\"agentKind\":\"ClaudeCode\"");
        json.ShouldContain("\"modelLevel\":\"Frontier\"");
        json.ShouldContain("\"agentKind\":\"Grok\"");
        json.ShouldContain("\"modelLevel\":null");

        var parsed = RoutingCandidate.Parse(json);
        parsed.Count.ShouldBe(2);
        parsed[0].Describe().ShouldBe("ClaudeCode/Frontier");
        parsed[1].Describe().ShouldBe("Grok");
    }
}

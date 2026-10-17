using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-1320 S1. The keep-six toggle is off until a Human PUT, and a missing row is not stored.
/// </summary>
[Category("Integration")]
[NotInParallel("KeepSixSettings")]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class KeepSixSettingsTests
{
    private readonly AntiphonWebAppFactory _factory;

    public KeepSixSettingsTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task get_lists_a_declared_orchestrator_as_off_with_target_six_and_creates_no_row()
    {
        var id = await InsertAgentAsync("keep-six-declared");

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/orchestrators/keep-six");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await FindAsync(response, id);
        listed.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        listed.GetProperty("target").GetInt32().ShouldBe(6);
        listed.GetProperty("revision").GetInt64().ShouldBe(0);
        (await ReadRowAsync(id)).ShouldBeNull();
    }

    [Test]
    public async Task put_human_enables_and_stores_target_reason_and_revision()
    {
        var id = await InsertAgentAsync("keep-six-put");
        using var client = _factory.CreateClient();

        using var created = await client.PutAsJsonAsync($"/api/orchestrators/{id}/keep-six", new
        {
            expectedRevision = 0,
            enabled = true,
            target = 4,
            reason = "operator toggle",
            provenance = "Human",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("target").GetInt32().ShouldBe(4);
        body.GetProperty("reason").GetString().ShouldBe("operator toggle");
        body.GetProperty("provenance").GetString().ShouldBe("Human");
        body.GetProperty("revision").GetInt64().ShouldBe(1);

        var stored = await ReadRowAsync(id);
        stored.ShouldNotBeNull();
        stored!.Enabled.ShouldBeTrue();
        stored.Target.ShouldBe(4);
        stored.Reason.ShouldBe("operator toggle");
        stored.Provenance.ShouldBe("Human");
        stored.Revision.ShouldBe(1);
        stored.EnabledAt.ShouldNotBeNull();

        using var stale = await client.PutAsJsonAsync($"/api/orchestrators/{id}/keep-six", new
        {
            expectedRevision = 0,
            enabled = false,
            target = 9,
            reason = "stale write",
            provenance = "Human",
        });
        ((int)stale.StatusCode).ShouldBe(409);
        var unchanged = await ReadRowAsync(id);
        unchanged!.Enabled.ShouldBeTrue();
        unchanged.Target.ShouldBe(4);
        unchanged.Reason.ShouldBe("operator toggle");
        unchanged.Revision.ShouldBe(1);
    }

    [Test]
    public async Task put_rejects_a_non_human_provenance()
    {
        var id = await InsertAgentAsync("keep-six-provenance");
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/orchestrators/{id}/keep-six", new
        {
            expectedRevision = 0,
            enabled = true,
            target = 6,
            reason = "not an operator",
            provenance = "Auto",
        });
        ((int)response.StatusCode).ShouldBe(400);
        (await ReadRowAsync(id)).ShouldBeNull();
    }

    [Test]
    public async Task put_rejects_a_target_outside_one_to_twenty_four()
    {
        var id = await InsertAgentAsync("keep-six-target");
        using var client = _factory.CreateClient();
        foreach (var target in new[] { 0, 25 })
        {
            using var response = await client.PutAsJsonAsync($"/api/orchestrators/{id}/keep-six", new
            {
                expectedRevision = 0,
                enabled = true,
                target,
                reason = "out of range",
                provenance = "Human",
            });
            ((int)response.StatusCode).ShouldBe(400);
        }

        (await ReadRowAsync(id)).ShouldBeNull();
    }

    [Test]
    public async Task put_on_a_pool_delegate_or_specialist_is_not_found()
    {
        var pool = await InsertAgentAsync("keep-six-pool", pool: true);
        var specialist = await InsertAgentAsync("keep-six-specialist", specialist: true);
        using var client = _factory.CreateClient();
        foreach (var id in new[] { pool, specialist })
        {
            using var response = await client.PutAsJsonAsync($"/api/orchestrators/{id}/keep-six", new
            {
                expectedRevision = 0,
                enabled = true,
                target = 6,
                reason = "not a standing orchestrator",
                provenance = "Human",
            });
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var text = await response.Content.ReadAsStringAsync();
            text.ShouldContain("not_found");
            text.ShouldContain(id.ToString());
            (await ReadRowAsync(id)).ShouldBeNull();
        }
    }

    private async Task<Guid> InsertAgentAsync(string name, bool pool = false, bool specialist = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Agents.Add(new Agent
        {
            Id = id,
            Name = name,
            Slug = "ks-" + id.ToString("N"),
            WorkingDirectory = "/tmp/keep-six",
            IsPoolDelegate = pool,
            StandingSpecialistRole = specialist ? AgentTaskRole.Check : null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentBundleAttachments.Add(new AgentBundleAttachment
        {
            AgentId = id,
            BundleKey = InstructionBundles.Orchestrator,
            Position = 0,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<JsonElement> FindAsync(HttpResponseMessage response, Guid agentId)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.ShouldBe(JsonValueKind.Array);
        foreach (var item in body.EnumerateArray())
        {
            if (item.GetProperty("agentId").GetGuid() == agentId)
                return item;
        }

        throw new ShouldAssertException($"GET /api/orchestrators/keep-six did not list {agentId}.");
    }

    private async Task<StoredKeepSix?> ReadRowAsync(Guid agentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        await using var exists = connection.CreateCommand();
        exists.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = 'KeepSixOrchestrators'
            """;
        if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0)
            return null;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Enabled", "Target", "Reason", "Provenance", "Revision", "EnabledAt"
            FROM "KeepSixOrchestrators"
            WHERE "AgentId" = @id
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = agentId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new StoredKeepSix(
            reader.GetBoolean(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTime>(5));
    }

    private sealed record StoredKeepSix(
        bool Enabled,
        int Target,
        string Reason,
        string Provenance,
        long Revision,
        DateTime? EnabledAt);
}

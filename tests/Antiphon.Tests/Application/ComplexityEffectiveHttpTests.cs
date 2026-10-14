using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<ComplexityChainApiWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class ComplexityEffectiveHttpTests(ComplexityChainApiWebAppFactory factory)
{
    [Test]
    public async Task Get_role_view_carries_effective_and_default_complexity_and_card_pins_are_excluded()
    {
        using var client = factory.CreateClient();
        Guid? stage = null, cardPin = null;
        try
        {
            await PutCellAsync(client, "Plan/Hard", "Codex", "first");
            stage = await PutPinAsync(client, new
            {
                role = "Plan", provenance = "Human", strength = "Required", agentKind = "ClaudeCode",
                modelLevel = "High", reason = "C097M V-B5",
            });
            Guid cardId;
            await using (var scope = factory.Services.CreateAsyncScope())
                cardId = (await RoutingPinServiceTests.SeedCardAsync(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(), "CARD-9097M")).Id;
            cardPin = await PutPinAsync(client, new
            {
                role = "Plan", card = cardId.ToString(), provenance = "Human", strength = "Required",
                agentKind = "Grok", modelLevel = "Frontier", reason = "C097M card exception",
            });
            var json = await GetAsync(client, "/api/complexity-chains?role=Plan");
            json.GetProperty("defaultComplexity").GetString().ShouldBe("Medium");
            var chains = json.GetProperty("chains");
            var hard = chains[0].GetProperty("effective");
            hard.GetProperty("source").GetString().ShouldBe("pin");
            hard.GetProperty("candidates").GetArrayLength().ShouldBe(1);
            var candidate = hard.GetProperty("candidates")[0];
            candidate.GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
            candidate.GetProperty("modelLevel").GetString().ShouldBe("High");
            candidate.GetProperty("alias").GetString().ShouldBe("opus");
            candidate.GetProperty("availableNow").GetBoolean().ShouldBeTrue();
            candidate.GetProperty("unavailableReason").ValueKind.ShouldBe(JsonValueKind.Null);
            hard.GetProperty("stagePin").GetProperty("id").GetGuid().ShouldBe(stage.Value);
            hard.GetProperty("stagePin").GetProperty("id").GetGuid().ShouldNotBe(cardPin.Value);
            hard.GetProperty("governsDefault").GetBoolean().ShouldBeFalse();
            chains[1].GetProperty("effective").GetProperty("source").GetString().ShouldBe("pin");
            chains[1].GetProperty("effective").GetProperty("governsDefault").GetBoolean().ShouldBeTrue();
            await VerifyIndependentReadsAsync(stage.Value);
            var holdId = Guid.NewGuid();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
                {
                    Id = holdId, Kind = AgentKind.ClaudeCode, ModelAlias = "opus",
                    Source = ModelAvailabilitySource.Manual, HitAt = DateTime.UtcNow, Reason = "C097M availability",
                });
                await db.SaveChangesAsync();
            }
            try
            {
                var held = (await GetAsync(client, "/api/complexity-chains?role=Plan"))
                    .GetProperty("chains")[0].GetProperty("effective").GetProperty("candidates")[0];
                held.GetProperty("availableNow").GetBoolean().ShouldBeFalse();
                held.GetProperty("unavailableReason").GetString().ShouldBe("held (manual, no re-enable time)");
            }
            finally
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
            }
            (await client.DeleteAsync($"/api/routing-pins/{stage}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var after = (await GetAsync(client, "/api/complexity-chains?role=Plan")).GetProperty("chains");
            var easy = after[2].GetProperty("effective");
            easy.GetProperty("source").GetString().ShouldBe("rolePolicy");
            easy.GetProperty("candidates")[0].GetProperty("alias").GetString().ShouldBe("fable");
            easy.GetProperty("stagePin").ValueKind.ShouldBe(JsonValueKind.Null);
            easy.GetProperty("withheldReason").ValueKind.ShouldBe(JsonValueKind.Null);
            after[0].GetProperty("effective").GetProperty("source").GetString().ShouldBe("role");
            var list = await GetAsync(client, "/api/complexity-chains");
            list.GetProperty("defaultComplexity").GetString().ShouldBe("Medium");
            foreach (var row in list.GetProperty("chains").EnumerateArray())
                row.GetProperty("effective").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Plan/Hard");
            if (stage is not null) await client.DeleteAsync($"/api/routing-pins/{stage}");
            if (cardPin is not null) await client.DeleteAsync($"/api/routing-pins/{cardPin}");
        }
    }

    [Test]
    public async Task Put_twice_is_last_writer_wins_without_a_409()
    {
        using var client = factory.CreateClient();
        try
        {
            var first = await PutCellAsync(client, "Debug/Easy", "Codex", "first");
            var updated = first.GetProperty("updatedAt").GetDateTime();
            await PutCellAsync(client, "Debug/Easy", "Grok", "second");
            var easy = (await GetAsync(client, "/api/complexity-chains?role=Debug")).GetProperty("chains")[2];
            easy.GetProperty("candidates").GetArrayLength().ShouldBe(1);
            easy.GetProperty("candidates")[0].GetProperty("agentKind").GetString().ShouldBe("Grok");
            easy.GetProperty("candidates")[0].GetProperty("modelLevel").GetString().ShouldBe("Frontier");
            easy.GetProperty("reason").GetString().ShouldBe("second");
            easy.GetProperty("updatedAt").GetDateTime().ShouldBeGreaterThanOrEqualTo(updated);
        }
        finally { await client.DeleteAsync("/api/complexity-chains/Debug/Easy"); }
    }

    [Test]
    public async Task Get_role_view_reports_an_agent_only_stage_pin_as_agent_bound()
    {
        using var client = factory.CreateClient();
        var agentId = Guid.NewGuid();
        Guid? pin = null;
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Agents.Add(new Agent
                {
                    Id = agentId, Name = "c097m-standing", Slug = "c097m-standing", WorkingDirectory = Path.GetTempPath(),
                    Details = "C097M V-B12", Status = AgentStatus.Idle, Kind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Medium, IsPoolDelegate = false,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            await PutCellAsync(client, "Plan/Medium", "Codex", "standing exception");
            pin = await PutPinAsync(client, new
            {
                role = "Plan", provenance = "Human", strength = "Required", agentId,
                reason = "C097M V-B12",
            });
            var chains = (await GetAsync(client, "/api/complexity-chains?role=Plan")).GetProperty("chains");
            foreach (var row in chains.EnumerateArray())
            {
                var effective = row.GetProperty("effective");
                effective.GetProperty("source").GetString().ShouldBe("agent");
                effective.GetProperty("candidates").GetArrayLength().ShouldBe(0);
                effective.GetProperty("stagePin").GetProperty("id").GetGuid().ShouldBe(pin.Value);
                effective.GetProperty("governsDefault").GetBoolean().ShouldBeFalse();
                effective.GetProperty("withheldReason").GetString().ShouldBe(
                    "stage pin binds standing agent c097m-standing (ClaudeCode); an ordinary dispatch runs on that agent and does not walk this row");
            }
            chains[1].GetProperty("candidates")[0].GetProperty("agentKind").GetString().ShouldBe("Codex");
            await using var verificationScope = factory.Services.CreateAsyncScope();
            var original = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fault = new ReadFault("Agents");
            await using var failedDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(original.Database.GetConnectionString()).AddInterceptors(fault).Options);
            var missingLabel = await Service(failedDb).GetEffectiveAsync(AgentTaskRole.Plan, TaskComplexity.Medium, CancellationToken.None);
            fault.Failures.ShouldBe(1);
            missingLabel.Candidates.Single().AgentKind.ShouldBe(AgentKind.Codex);
            missingLabel.Effective.ShouldNotBeNull().Source.ShouldBe("agent");
            missingLabel.Effective.Candidates.ShouldBeEmpty();
            missingLabel.Effective.GovernsDefault.ShouldBeFalse();
            missingLabel.Effective.StagePin.ShouldNotBeNull().Id.ShouldBe(pin.Value);
            missingLabel.Effective.WithheldReason.ShouldContain("standing agent read failed (InvalidOperationException)");
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Plan/Medium");
            if (pin is not null) await client.DeleteAsync($"/api/routing-pins/{pin}");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FindAsync(agentId);
            if (agent is not null) { db.Agents.Remove(agent); await db.SaveChangesAsync(); }
        }
    }

    // The HTTP method owns these seeds. Fresh contexts cut one query at a time; siblings and
    // later tiers must survive, and a missing optional service keeps the direct-constructor path.
    private async Task VerifyIndependentReadsAsync(Guid stage)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var original = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (table, reason, source) in new[]
        {
            ("RoutingPins", "stage pin read failed (InvalidOperationException)", "role"),
            ("ComplexityChains", "chain read failed (InvalidOperationException)", "pin"),
            ("ModelAvailabilityHolds", "availability read failed (InvalidOperationException)", "pin"),
        })
        {
            var fault = new ReadFault(table);
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(original.Database.GetConnectionString()).AddInterceptors(fault).Options);
            var rows = await Service(db).ListAsync(AgentTaskRole.Plan, CancellationToken.None);
            fault.Failures.ShouldBe(1);
            rows.Chains.Count.ShouldBe(3);
            var hard = rows.Chains[0];
            hard.Effective.ShouldNotBeNull().Source.ShouldBe(source);
            hard.Effective.WithheldReason.ShouldBe(reason);
            if (table == "ComplexityChains") hard.Candidates.ShouldBeEmpty();
            else hard.Candidates.Single().AgentKind.ShouldBe(AgentKind.Codex);
            if (table == "RoutingPins") hard.Effective.StagePin.ShouldBeNull();
            else hard.Effective.StagePin.ShouldNotBeNull().Id.ShouldBe(stage);
            hard.Effective.Candidates.Single().AgentKind.ShouldBe(table == "RoutingPins" ? AgentKind.Codex : AgentKind.ClaudeCode);
            if (table == "ModelAvailabilityHolds")
            {
                hard.Candidates.Single().AvailableNow.ShouldBeFalse();
                hard.Candidates.Single().UnavailableReason.ShouldBe(reason);
            }
            rows.Chains[1].Effective.ShouldNotBeNull().Source.ShouldBe("pin");
            rows.Chains[1].Effective!.WithheldReason.ShouldBeNull();
        }
        var settings = new DelegationSettings { DefaultComplexity = TaskComplexity.Easy };
        var configured = await Service(original, settings).ListAsync(AgentTaskRole.Plan, CancellationToken.None);
        configured.DefaultComplexity.ShouldBe(TaskComplexity.Easy);
        configured.Chains.Select(c => c.Effective!.GovernsDefault).ShouldBe([false, false, true]);
    }

    private static ComplexityChainService Service(AppDbContext db, DelegationSettings? settings = null)
    {
        var options = Options.Create(settings ?? new DelegationSettings());
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        var routing = new ComplexityRoutingService(db, options, TimeProvider.System, availability);
        var pins = new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance, routing);
        return new(db, TimeProvider.System, routing, options, NullLogger<ComplexityChainService>.Instance, pins);
    }

    private sealed class ReadFault(string table) : DbCommandInterceptor
    {
        public int Failures { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Failures == 0 && command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal))
            {
                Failures++;
                throw new InvalidOperationException("C097M injected read failure");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> PutCellAsync(HttpClient client, string cell, string kind, string reason)
    {
        using var response = await client.PutAsJsonAsync($"/api/complexity-chains/{cell}", new
        {
            candidates = new[] { new { agentKind = kind, modelLevel = "Frontier" } }, provenance = "Human", reason,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> PutPinAsync(HttpClient client, object body)
    {
        using var response = await client.PutAsJsonAsync("/api/routing-pins", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}

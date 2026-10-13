using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-0097 S1: GET /api/role-policy over real HTTP. Oracles are literal JSON values (F-1 in
/// docs/superpowers/plans/2026-10-13-card-0097-test-design.md).
/// </summary>
[Category("Integration")]
[NotInParallel("RolePolicyEndpoints")]
[ClassDataSource<RolePolicyApiWebAppFactory>(Shared = SharedType.PerClass)]
public sealed class RolePolicyEndpointTests
{
    private readonly RolePolicyApiWebAppFactory _factory;

    public RolePolicyEndpointTests(RolePolicyApiWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Get_lists_every_routable_role_in_order_with_policy_fields()
    {
        using var client = _factory.CreateClient();

        var body = await GetPolicyAsync(client);

        body.GetProperty("defaultLevel").GetString().ShouldBe("High");
        body.GetProperty("defaultTimeoutMinutes").GetInt32().ShouldBe(240);
        var roles = body.GetProperty("roles").EnumerateArray().ToList();
        roles.Select(r => r.GetProperty("role").GetString()).ShouldBe(
        [
            "Investigate", "Plan", "TestDesign", "Code", "Mutation", "Review", "Debug",
            "Coverage", "Docs", "Commit", "Test", "Deploy", "Merge", "Custom",
        ]);

        var mutation = Row(roles, "Mutation");
        mutation.GetProperty("configured").GetBoolean().ShouldBeTrue();
        mutation.GetProperty("level").GetString().ShouldBe("Low");
        mutation.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.Null);
        mutation.GetProperty("alias").GetString().ShouldBe("haiku");
        mutation.GetProperty("escalateTo").GetString().ShouldBe("High");
        mutation.GetProperty("escalateAfterMinutes").ValueKind.ShouldBe(JsonValueKind.Null);
        mutation.GetProperty("timeoutMinutes").GetInt32().ShouldBe(240);
        mutation.GetProperty("stagePin").ValueKind.ShouldBe(JsonValueKind.Null);
        var effective = mutation.GetProperty("effective");
        effective.GetProperty("source").GetString().ShouldBe("rolePolicy");
        effective.GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
        effective.GetProperty("modelLevel").GetString().ShouldBe("Low");
        effective.GetProperty("alias").GetString().ShouldBe("haiku");
        effective.GetProperty("strength").ValueKind.ShouldBe(JsonValueKind.Null);

        var custom = Row(roles, "Custom");
        custom.GetProperty("configured").GetBoolean().ShouldBeFalse();
        custom.GetProperty("level").GetString().ShouldBe("High");
        custom.GetProperty("alias").GetString().ShouldBe("opus");
        custom.GetProperty("escalateTo").ValueKind.ShouldBe(JsonValueKind.Null);
        custom.GetProperty("timeoutMinutes").GetInt32().ShouldBe(240);
        custom.GetProperty("stagePin").ValueKind.ShouldBe(JsonValueKind.Null);

        Row(roles, "Test").GetProperty("escalateTo").GetString().ShouldBe("Medium");
        Row(roles, "Docs").GetProperty("alias").GetString().ShouldBe("sonnet");
    }

    [Test]
    public async Task Get_overlays_the_active_stage_pin_as_effective()
    {
        using var client = _factory.CreateClient();
        string? pinId = null;
        try
        {
            using var put = await client.PutAsJsonAsync(
                "/api/routing-pins",
                new
                {
                    role = "Plan",
                    provenance = "Human",
                    strength = "Required",
                    candidates = new[] { new { agentKind = "Codex", modelLevel = "Frontier" } },
                    reason = "C097 V-2",
                });
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            pinId = (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

            var roles = (await GetPolicyAsync(client)).GetProperty("roles").EnumerateArray().ToList();

            var plan = Row(roles, "Plan");
            plan.GetProperty("level").GetString().ShouldBe("Frontier");
            plan.GetProperty("alias").GetString().ShouldBe("fable");
            var stagePin = plan.GetProperty("stagePin");
            stagePin.GetProperty("id").GetString().ShouldBe(pinId);
            stagePin.GetProperty("candidateCount").GetInt32().ShouldBe(1);
            var head = stagePin.GetProperty("candidates")[0];
            head.GetProperty("alias").GetString().ShouldBe("gpt-6-astra");
            head.GetProperty("availableNow").GetBoolean().ShouldBeTrue();
            var effective = plan.GetProperty("effective");
            effective.GetProperty("source").GetString().ShouldBe("pin");
            effective.GetProperty("agentKind").GetString().ShouldBe("Codex");
            effective.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
            effective.GetProperty("alias").GetString().ShouldBe("gpt-6-astra");
            effective.GetProperty("strength").GetString().ShouldBe("Required");

            var code = Row(roles, "Code");
            code.GetProperty("stagePin").ValueKind.ShouldBe(JsonValueKind.Null);
            code.GetProperty("effective").GetProperty("source").GetString().ShouldBe("rolePolicy");
        }
        finally
        {
            if (pinId is not null)
                await client.DeleteAsync($"/api/routing-pins/{pinId}");
        }
    }

    [Test]
    public async Task Get_ignores_card_pins_and_cleared_pins()
    {
        Guid cardId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cardId = (await RoutingPinServiceTests.SeedCardAsync(db, "CARD-9097")).Id;
        }

        using var client = _factory.CreateClient();
        string? cardPinId = null;
        try
        {
            using var cardPut = await client.PutAsJsonAsync(
                "/api/routing-pins",
                new
                {
                    role = "Code",
                    card = cardId.ToString(),
                    provenance = "Human",
                    strength = "Required",
                    agentKind = "Grok",
                    modelLevel = "Frontier",
                    reason = "C097 V-3 card pin",
                });
            cardPut.StatusCode.ShouldBe(HttpStatusCode.OK);
            cardPinId = (await cardPut.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

            using var stagePut = await client.PutAsJsonAsync(
                "/api/routing-pins",
                new
                {
                    role = "Debug",
                    provenance = "Human",
                    strength = "Preferred",
                    agentKind = "ClaudeCode",
                    modelLevel = "High",
                    reason = "C097 V-3 cleared stage pin",
                });
            stagePut.StatusCode.ShouldBe(HttpStatusCode.OK);
            var stagePinId = (await stagePut.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
            using var clear = await client.DeleteAsync($"/api/routing-pins/{stagePinId}");
            clear.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Control: the card pin is live, so its absence below is the stage filter, not a lost write.
            using var pins = await client.GetAsync("/api/routing-pins");
            pins.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await pins.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pins").EnumerateArray()
                .Select(p => p.GetProperty("id").GetString())
                .ShouldContain(cardPinId);

            var roles = (await GetPolicyAsync(client)).GetProperty("roles").EnumerateArray().ToList();

            var code = Row(roles, "Code");
            code.GetProperty("stagePin").ValueKind.ShouldBe(JsonValueKind.Null);
            code.GetProperty("effective").GetProperty("source").GetString().ShouldBe("rolePolicy");
            code.GetProperty("effective").GetProperty("alias").GetString().ShouldBe("fable");
            var debug = Row(roles, "Debug");
            debug.GetProperty("stagePin").ValueKind.ShouldBe(JsonValueKind.Null);
            debug.GetProperty("effective").GetProperty("source").GetString().ShouldBe("rolePolicy");
        }
        finally
        {
            if (cardPinId is not null)
                await client.DeleteAsync($"/api/routing-pins/{cardPinId}");
        }
    }

    /// <summary>
    /// A host bound with Docs Level=999, Commit Kind=Raw and Deploy Kind=999 (config validation admits
    /// all three) still answers 200 with 14 rows; the bad rows withhold their target.
    /// </summary>
    [Test]
    [ClassDataSource<RolePolicyUnsupportedValuesWebAppFactory>(Shared = SharedType.PerClass)]
    public async Task Get_serialises_every_row_when_one_role_has_unsupported_values(
        RolePolicyUnsupportedValuesWebAppFactory factory)
    {
        using var client = factory.CreateClient();

        var roles = (await GetPolicyAsync(client)).GetProperty("roles").EnumerateArray().ToList();

        roles.Count.ShouldBe(14);
        var docs = Row(roles, "Docs");
        docs.GetProperty("level").GetString().ShouldBe("High");
        docs.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.Null);
        docs.GetProperty("alias").GetString().ShouldBe("");
        AssertWithheld(docs.GetProperty("effective"));
        var commit = Row(roles, "Commit");
        commit.GetProperty("kind").GetString().ShouldBe("Raw");
        commit.GetProperty("level").GetString().ShouldBe("Medium");
        commit.GetProperty("alias").GetString().ShouldBe("");
        AssertWithheld(commit.GetProperty("effective"));
        var deploy = Row(roles, "Deploy");
        deploy.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.Null);
        deploy.GetProperty("level").GetString().ShouldBe("Low");
        deploy.GetProperty("alias").GetString().ShouldBe("");
        AssertWithheld(deploy.GetProperty("effective"));

        var mutation = Row(roles, "Mutation").GetProperty("effective");
        mutation.GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
        mutation.GetProperty("modelLevel").GetString().ShouldBe("Low");
        mutation.GetProperty("alias").GetString().ShouldBe("haiku");
        Row(roles, "Test").GetProperty("alias").GetString().ShouldBe("haiku");
    }

    private static void AssertWithheld(JsonElement effective)
    {
        effective.GetProperty("source").GetString().ShouldBe("rolePolicy");
        effective.GetProperty("agentKind").ValueKind.ShouldBe(JsonValueKind.Null);
        effective.GetProperty("modelLevel").ValueKind.ShouldBe(JsonValueKind.Null);
        effective.GetProperty("alias").ValueKind.ShouldBe(JsonValueKind.Null);
        effective.GetProperty("strength").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task<JsonElement> GetPolicyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/role-policy");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Row(IEnumerable<JsonElement> roles, string role) =>
        roles.Single(r => r.GetProperty("role").GetString() == role);
}

public sealed class RolePolicyApiWebAppFactory : AntiphonWebAppFactory;

public sealed class RolePolicyUnsupportedValuesWebAppFactory : AntiphonWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Delegation:RolePolicy:Docs:Level"] = "999",
                ["Delegation:RolePolicy:Commit:Kind"] = "Raw",
                ["Delegation:RolePolicy:Deploy:Kind"] = "999",
            }));
    }
}

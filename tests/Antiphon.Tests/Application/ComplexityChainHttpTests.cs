using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S1 HTTP: GET/PUT/DELETE camelCase, 409 Auto-over-Human, 422 empty/duplicate,
/// GET shows per-candidate availableNow.
/// </summary>
[NotInParallel]
[ClassDataSource<ComplexityChainApiWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class ComplexityChainHttpTests
{
    private readonly ComplexityChainApiWebAppFactory _factory;

    public ComplexityChainHttpTests(ComplexityChainApiWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Put_get_delete_round_trip_is_camelCase_and_double_clear_is_204()
    {
        using var client = _factory.CreateClient();
        try
        {
            var put = await client.PutAsJsonAsync(
                "/api/complexity-chains/Hard",
                new
                {
                    candidates = new[]
                    {
                        new { agentKind = "ClaudeCode", modelLevel = "Frontier" },
                        new { agentKind = "Grok", modelLevel = "Frontier" },
                    },
                    provenance = "Human",
                    reason = "http pin",
                });
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            var putJson = await put.Content.ReadFromJsonAsync<JsonElement>();
            putJson.GetProperty("complexity").GetString().ShouldBe("Hard");
            putJson.GetProperty("source").GetString().ShouldBe("pin");
            putJson.GetProperty("provenance").GetString().ShouldBe("Human");
            putJson.GetProperty("reason").GetString().ShouldBe("http pin");
            var candidates = putJson.GetProperty("candidates").EnumerateArray().ToList();
            candidates.Count.ShouldBe(2);
            candidates[0].GetProperty("alias").GetString().ShouldBe("fable");
            candidates[0].GetProperty("availableNow").GetBoolean().ShouldBeTrue();
            candidates[1].GetProperty("alias").GetString().ShouldBe("grok-4.6");

            var get = await client.GetAsync("/api/complexity-chains");
            get.StatusCode.ShouldBe(HttpStatusCode.OK);
            var list = await get.Content.ReadFromJsonAsync<JsonElement>();
            var chains = list.GetProperty("chains").EnumerateArray().ToList();
            chains.Count.ShouldBe(3);
            chains.Select(c => c.GetProperty("complexity").GetString())
                .ShouldBe(["Hard", "Medium", "Easy"]);
            chains.Single(c => c.GetProperty("complexity").GetString() == "Medium")
                .GetProperty("source").GetString().ShouldBe("config");

            var del = await client.DeleteAsync("/api/complexity-chains/Hard");
            del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var delAgain = await client.DeleteAsync("/api/complexity-chains/Hard");
            delAgain.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var after = await client.GetAsync("/api/complexity-chains");
            var afterList = await after.Content.ReadFromJsonAsync<JsonElement>();
            afterList.GetProperty("chains").EnumerateArray()
                .Single(c => c.GetProperty("complexity").GetString() == "Hard")
                .GetProperty("source").GetString().ShouldBe("config");
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Hard");
        }
    }

    [Test]
    public async Task Auto_over_human_is_409_and_empty_or_duplicate_is_422()
    {
        using var client = _factory.CreateClient();
        try
        {
            var human = await client.PutAsJsonAsync(
                "/api/complexity-chains/Easy",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                    reason = "operator: cheap checks",
                });
            human.StatusCode.ShouldBe(HttpStatusCode.OK);

            var auto = await client.PutAsJsonAsync(
                "/api/complexity-chains/Easy",
                new
                {
                    candidates = new[] { new { agentKind = "Codex", modelLevel = "Medium" } },
                    provenance = "Auto",
                });
            auto.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var autoBody = await auto.Content.ReadFromJsonAsync<JsonElement>();
            autoBody.GetProperty("code").GetString().ShouldBe("complexity_chain_human");

            var empty = await client.PutAsJsonAsync(
                "/api/complexity-chains/Medium",
                new { candidates = Array.Empty<object>(), provenance = "Human" });
            empty.StatusCode.ShouldBe((HttpStatusCode)422);

            var dup = await client.PutAsJsonAsync(
                "/api/complexity-chains/Medium",
                new
                {
                    candidates = new[]
                    {
                        new { agentKind = "Grok", modelLevel = "Frontier" },
                        new { agentKind = "Grok", modelLevel = "Frontier" },
                    },
                    provenance = "Human",
                });
            dup.StatusCode.ShouldBe((HttpStatusCode)422);

            var unknown = await client.PutAsJsonAsync(
                "/api/complexity-chains/Legendary",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                });
            unknown.StatusCode.ShouldBe((HttpStatusCode)422);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Easy");
            await client.DeleteAsync("/api/complexity-chains/Medium");
        }
    }
}

public sealed class ComplexityChainApiWebAppFactory : AntiphonWebAppFactory;

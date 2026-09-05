using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0332 S1 HTTP: three-segment PUT/DELETE, ?role= effective view, two-segment alias,
/// roles[] camelCase, D6 409, role 422s.
/// </summary>
[NotInParallel]
[ClassDataSource<ComplexityChainApiWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class ComplexityChainRoleHttpTests
{
    private readonly ComplexityChainApiWebAppFactory _factory;

    public ComplexityChainRoleHttpTests(ComplexityChainApiWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Put_get_delete_cell_and_two_segment_alias_write_any_role()
    {
        using var client = _factory.CreateClient();
        try
        {
            var cell = await client.PutAsJsonAsync(
                "/api/complexity-chains/Plan/Hard",
                new
                {
                    candidates = new[] { new { agentKind = "Codex", modelLevel = "Frontier" } },
                    provenance = "Human",
                    reason = "Plan cell",
                });
            cell.StatusCode.ShouldBe(HttpStatusCode.OK);
            var cellJson = await cell.Content.ReadFromJsonAsync<JsonElement>();
            cellJson.GetProperty("role").GetString().ShouldBe("Plan");
            cellJson.GetProperty("complexity").GetString().ShouldBe("Hard");
            cellJson.GetProperty("resolvedFrom").GetString().ShouldBe("role");
            cellJson.GetProperty("source").GetString().ShouldBe("pin");

            var any = await client.PutAsJsonAsync(
                "/api/complexity-chains/Hard",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                    reason = "any-role via alias",
                });
            any.StatusCode.ShouldBe(HttpStatusCode.OK);
            var anyJson = await any.Content.ReadFromJsonAsync<JsonElement>();
            anyJson.TryGetProperty("role", out var roleEl).ShouldBeTrue();
            roleEl.ValueKind.ShouldBe(JsonValueKind.Null);
            anyJson.GetProperty("resolvedFrom").GetString().ShouldBe("any");

            var list = await client.GetAsync("/api/complexity-chains");
            list.StatusCode.ShouldBe(HttpStatusCode.OK);
            var listJson = await list.Content.ReadFromJsonAsync<JsonElement>();
            var roles = listJson.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToList();
            roles[0].ShouldBe("Plan");
            roles.ShouldContain("Custom");
            roles.ShouldNotContain("Check");
            listJson.GetProperty("complexities").EnumerateArray().Select(e => e.GetString())
                .ShouldBe(["Hard", "Medium", "Easy"]);
            var chains = listJson.GetProperty("chains").EnumerateArray().ToList();
            chains.Count.ShouldBe(4);
            chains[0].GetProperty("complexity").GetString().ShouldBe("Hard");
            chains[0].GetProperty("role").ValueKind.ShouldBe(JsonValueKind.Null);
            chains[3].GetProperty("role").GetString().ShouldBe("Plan");
            chains[3].GetProperty("resolvedFrom").GetString().ShouldBe("role");

            var delCell = await client.DeleteAsync("/api/complexity-chains/Plan/Hard");
            delCell.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var delAlias = await client.DeleteAsync("/api/complexity-chains/Hard");
            delAlias.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var delAgain = await client.DeleteAsync("/api/complexity-chains/any/Hard");
            delAgain.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Plan/Hard");
            await client.DeleteAsync("/api/complexity-chains/any/Hard");
            await client.DeleteAsync("/api/complexity-chains/Hard");
        }
    }

    [Test]
    public async Task Get_role_Plan_returns_effective_resolvedFrom()
    {
        using var client = _factory.CreateClient();
        try
        {
            (await client.PutAsJsonAsync(
                "/api/complexity-chains/Plan/Hard",
                new
                {
                    candidates = new[] { new { agentKind = "Codex", modelLevel = "Frontier" } },
                    provenance = "Human",
                })).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await client.PutAsJsonAsync(
                "/api/complexity-chains/any/Medium",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                })).StatusCode.ShouldBe(HttpStatusCode.OK);

            var get = await client.GetAsync("/api/complexity-chains?role=Plan");
            get.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = await get.Content.ReadFromJsonAsync<JsonElement>();
            var chains = json.GetProperty("chains").EnumerateArray().ToList();
            chains.Count.ShouldBe(3);
            chains[0].GetProperty("complexity").GetString().ShouldBe("Hard");
            chains[0].GetProperty("role").GetString().ShouldBe("Plan");
            chains[0].GetProperty("resolvedFrom").GetString().ShouldBe("role");
            chains[1].GetProperty("complexity").GetString().ShouldBe("Medium");
            chains[1].GetProperty("resolvedFrom").GetString().ShouldBe("any");
            chains[2].GetProperty("complexity").GetString().ShouldBe("Easy");
            chains[2].GetProperty("resolvedFrom").GetString().ShouldBe("none");
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Plan/Hard");
            await client.DeleteAsync("/api/complexity-chains/any/Medium");
            await client.DeleteAsync("/api/complexity-chains/Medium");
        }
    }

    [Test]
    public async Task Auto_cell_under_Human_any_role_is_409_and_specialist_or_unknown_is_422()
    {
        using var client = _factory.CreateClient();
        try
        {
            (await client.PutAsJsonAsync(
                "/api/complexity-chains/any/Easy",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                    reason = "operator any-role Easy",
                })).StatusCode.ShouldBe(HttpStatusCode.OK);

            var auto = await client.PutAsJsonAsync(
                "/api/complexity-chains/Plan/Easy",
                new
                {
                    candidates = new[] { new { agentKind = "Codex", modelLevel = "Medium" } },
                    provenance = "Auto",
                });
            auto.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var autoBody = await auto.Content.ReadFromJsonAsync<JsonElement>();
            autoBody.GetProperty("code").GetString().ShouldBe("complexity_chain_human");

            var check = await client.PutAsJsonAsync(
                "/api/complexity-chains/Check/Hard",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                });
            check.StatusCode.ShouldBe((HttpStatusCode)422);

            var unknown = await client.PutAsJsonAsync(
                "/api/complexity-chains/Wizard/Hard",
                new
                {
                    candidates = new[] { new { agentKind = "Grok", modelLevel = "Frontier" } },
                    provenance = "Human",
                });
            unknown.StatusCode.ShouldBe((HttpStatusCode)422);

            var getCheck = await client.GetAsync("/api/complexity-chains?role=Diagnose");
            getCheck.StatusCode.ShouldBe((HttpStatusCode)422);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/any/Easy");
            await client.DeleteAsync("/api/complexity-chains/Easy");
            await client.DeleteAsync("/api/complexity-chains/Plan/Easy");
        }
    }
}

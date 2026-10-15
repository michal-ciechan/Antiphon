using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1275: PUT/DELETE /api/complexity-chains carry the cell row's <c>concurrencyToken</c>.
/// A stale token is 409 <c>complexity_chain_stale</c> with the current row; the empty GUID
/// means "expect no active row"; an omitted token writes without a version check (sequential
/// requests here; the race paths are in <see cref="ComplexityChainRaceTests"/>).
/// </summary>
[NotInParallel]
[ClassDataSource<ComplexityChainConcurrencyWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class ComplexityChainConcurrencyHttpTests
{
    private const string Absent = "00000000-0000-0000-0000-000000000000";
    private readonly ComplexityChainConcurrencyWebAppFactory _factory;

    public ComplexityChainConcurrencyHttpTests(ComplexityChainConcurrencyWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Stale_put_is_409_with_the_current_row_and_leaves_it_unchanged()
    {
        using var client = _factory.CreateClient();
        try
        {
            var first = await PutAsync(client, "any/Hard", "Grok", "opened", Absent);
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            var opened = await TokenAsync(first);

            var other = await PutAsync(client, "any/Hard", "Codex", "other operator", opened);
            other.StatusCode.ShouldBe(HttpStatusCode.OK);
            var current = await TokenAsync(other);
            current.ShouldNotBe(opened);

            var stale = await PutAsync(client, "any/Hard", "ClaudeCode", "stale editor", opened);
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var body = await stale.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().ShouldBe("complexity_chain_stale");
            var row = body.GetProperty("current");
            row.GetProperty("concurrencyToken").GetString().ShouldBe(current);
            row.GetProperty("reason").GetString().ShouldBe("other operator");
            row.GetProperty("candidates")[0].GetProperty("agentKind").GetString().ShouldBe("Codex");

            var stored = await CellAsync(client, role: null, "Hard");
            stored.GetProperty("reason").GetString().ShouldBe("other operator");
            stored.GetProperty("concurrencyToken").GetString().ShouldBe(current);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/any/Hard");
        }
    }

    [Test]
    public async Task Stale_delete_is_409_and_keeps_the_row()
    {
        using var client = _factory.CreateClient();
        try
        {
            var opened = await TokenAsync(await PutAsync(client, "Plan/Medium", "Grok", "opened", Absent));
            var current = await TokenAsync(await PutAsync(client, "Plan/Medium", "Codex", "changed", opened));

            var stale = await client.DeleteAsync($"/api/complexity-chains/Plan/Medium?concurrencyToken={opened}");
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var body = await stale.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().ShouldBe("complexity_chain_stale");
            body.GetProperty("current").GetProperty("concurrencyToken").GetString().ShouldBe(current);
            (await CellAsync(client, "Plan", "Medium")).GetProperty("resolvedFrom").GetString().ShouldBe("role");

            var fresh = await client.DeleteAsync($"/api/complexity-chains/Plan/Medium?concurrencyToken={current}");
            fresh.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var cleared = await CellAsync(client, "Plan", "Medium");
            cleared.GetProperty("resolvedFrom").GetString().ShouldBe("none");
            cleared.GetProperty("concurrencyToken").ValueKind.ShouldBe(JsonValueKind.Null);

            // Already clear is still a no-op, so a script or a second tab can run it twice.
            var again = await client.DeleteAsync($"/api/complexity-chains/Plan/Medium?concurrencyToken={current}");
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Plan/Medium");
        }
    }

    [Test]
    public async Task Create_when_absent_needs_no_version_and_the_empty_token_refuses_an_existing_row()
    {
        using var client = _factory.CreateClient();
        try
        {
            // Legacy caller: no token on an absent cell creates it.
            var legacy = await PutAsync(client, "any/Easy", "Grok", "legacy create", token: null);
            legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
            var created = await TokenAsync(legacy);

            // An editor opened on the empty cell must not overwrite a row created since.
            var expectAbsent = await PutAsync(client, "any/Easy", "Codex", "empty editor", Absent);
            expectAbsent.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var body = await expectAbsent.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().ShouldBe("complexity_chain_stale");
            body.GetProperty("current").GetProperty("concurrencyToken").GetString().ShouldBe(created);

            // Legacy caller with no token still overwrites (complexity-chain.ps1 before CARD-1275).
            var overwrite = await PutAsync(client, "any/Easy", "Codex", "legacy overwrite", token: null);
            overwrite.StatusCode.ShouldBe(HttpStatusCode.OK);
            var last = await TokenAsync(overwrite);
            last.ShouldNotBe(created);

            // A token for a row that was cleared since is stale too: the current state is config.
            (await client.DeleteAsync("/api/complexity-chains/any/Easy")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var gone = await PutAsync(client, "any/Easy", "Grok", "editor of a cleared row", last);
            gone.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var goneBody = await gone.Content.ReadFromJsonAsync<JsonElement>();
            goneBody.GetProperty("current").GetProperty("source").GetString().ShouldBe("config");
            goneBody.GetProperty("current").GetProperty("concurrencyToken").ValueKind.ShouldBe(JsonValueKind.Null);
            (await CellAsync(client, role: null, "Easy")).GetProperty("source").GetString().ShouldBe("config");
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/any/Easy");
        }
    }

    [Test]
    public async Task Concurrent_writers_with_one_token_leave_exactly_one_winner()
    {
        using var client = _factory.CreateClient();
        try
        {
            // Concurrent creates on an empty cell: the unique active-row index decides, as a 409.
            var creates = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
                PutAsync(client, "Code/Hard", "Grok", $"create {i}", Absent)));
            creates.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            creates.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
            var token = await TokenAsync(creates.Single(r => r.StatusCode == HttpStatusCode.OK));

            // Concurrent updates from one opened version: one wins, the rest see the new row.
            var updates = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
                PutAsync(client, "Code/Hard", "Codex", $"update {i}", token)));
            updates.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            updates.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
            var winner = await updates.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();
            var stored = await CellAsync(client, "Code", "Hard");
            stored.GetProperty("reason").GetString().ShouldBe(winner.GetProperty("reason").GetString());
            stored.GetProperty("concurrencyToken").GetString().ShouldBe(winner.GetProperty("concurrencyToken").GetString());
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/Code/Hard");
        }
    }

    [Test]
    public async Task Effective_view_carries_the_cells_own_token_not_the_any_role_rows()
    {
        using var client = _factory.CreateClient();
        try
        {
            var anyToken = await TokenAsync(await PutAsync(client, "any/Medium", "Grok", "any medium", Absent));
            var cellToken = await TokenAsync(await PutAsync(client, "Review/Hard", "Codex", "review hard", Absent));

            var view = await client.GetFromJsonAsync<JsonElement>("/api/complexity-chains?role=Review");
            var chains = view.GetProperty("chains").EnumerateArray().ToList();
            var hard = chains.Single(c => c.GetProperty("complexity").GetString() == "Hard");
            hard.GetProperty("resolvedFrom").GetString().ShouldBe("role");
            hard.GetProperty("concurrencyToken").GetString().ShouldBe(cellToken);
            var medium = chains.Single(c => c.GetProperty("complexity").GetString() == "Medium");
            medium.GetProperty("resolvedFrom").GetString().ShouldBe("any");
            medium.GetProperty("concurrencyToken").ValueKind.ShouldBe(JsonValueKind.Null);

            var list = await client.GetFromJsonAsync<JsonElement>("/api/complexity-chains");
            list.GetProperty("chains").EnumerateArray()
                .Single(c => c.GetProperty("complexity").GetString() == "Medium"
                    && c.GetProperty("role").ValueKind == JsonValueKind.Null)
                .GetProperty("concurrencyToken").GetString().ShouldBe(anyToken);
        }
        finally
        {
            await client.DeleteAsync("/api/complexity-chains/any/Medium");
            await client.DeleteAsync("/api/complexity-chains/Review/Hard");
        }
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client, string cell, string kind, string reason, string? token) =>
        client.PutAsJsonAsync(
            $"/api/complexity-chains/{cell}",
            new
            {
                candidates = new[] { new { agentKind = kind, modelLevel = "Frontier" } },
                provenance = "Human",
                reason,
                concurrencyToken = token,
            });

    private static async Task<string> TokenAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("concurrencyToken").GetString().ShouldNotBeNull();
        token.ShouldNotBe(Absent);
        return token;
    }

    private static async Task<JsonElement> CellAsync(HttpClient client, string? role, string complexity)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/complexity-chains");
        var chains = list.GetProperty("chains").EnumerateArray().ToList();
        if (role is null)
        {
            return chains.Single(c => c.GetProperty("complexity").GetString() == complexity
                && (!c.TryGetProperty("role", out var r) || r.ValueKind == JsonValueKind.Null));
        }

        var cell = chains.SingleOrDefault(c => c.GetProperty("complexity").GetString() == complexity
            && c.TryGetProperty("role", out var r) && r.GetString() == role);
        if (cell.ValueKind != JsonValueKind.Undefined)
            return cell;
        var effective = await client.GetFromJsonAsync<JsonElement>($"/api/complexity-chains?role={role}");
        return effective.GetProperty("chains").EnumerateArray()
            .Single(c => c.GetProperty("complexity").GetString() == complexity);
    }
}

public sealed class ComplexityChainConcurrencyWebAppFactory : AntiphonWebAppFactory;

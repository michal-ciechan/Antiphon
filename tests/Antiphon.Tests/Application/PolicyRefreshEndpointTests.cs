using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0334 S3 — HTTP front door for policy refresh: PATCH mode round-trip and 409
/// <c>not_resumable</c> when there is no live session. Working-session 409s and the force
/// path live in <see cref="PolicyRefreshServiceTests"/> (they need a live fake adapter).
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class PolicyRefreshEndpointTests
{
    private readonly AntiphonWebAppFactory _factory;
    private readonly List<Guid> _createdAgentIds = [];

    public PolicyRefreshEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_createdAgentIds.Count == 0)
            return;

        using var client = _factory.CreateClient();
        foreach (var id in _createdAgentIds)
            await client.DeleteAsync($"/api/agents/{id}");
        _createdAgentIds.Clear();
    }

    [Test]
    public async Task Patch_policyRefreshMode_round_trips_on_the_detail_dto()
    {
        var created = await CreateAgentAsync();
        var id = created.GetProperty("id").GetGuid();

        using var client = _factory.CreateClient();
        var patch = await client.PatchAsJsonAsync(
            $"/api/agents/{id}",
            new
            {
                name = created.GetProperty("name").GetString(),
                workingDirectory = created.GetProperty("workingDirectory").GetString(),
                details = created.GetProperty("details").GetString(),
                assignmentPolicy = "AutoPick",
                policyRefreshMode = "Notify",
            });

        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("policyDrift").GetProperty("mode").GetString().ShouldBe("Notify");

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/agents/{id}");
        fetched.GetProperty("policyDrift").GetProperty("mode").GetString().ShouldBe("Notify");
    }

    [Test]
    public async Task Refresh_policy_without_a_live_session_is_409_not_resumable()
    {
        var created = await CreateAgentAsync();
        var id = created.GetProperty("id").GetGuid();

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/agents/{id}/refresh-policy",
            new { force = false });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().ShouldBe(409);
        problem.GetProperty("code").GetString().ShouldBe("not_resumable");
    }

    private async Task<JsonElement> CreateAgentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents", new
        {
            name = $"CARD-0334 {suffix}",
            workingDirectory = $"D:/src/card-0334-{suffix}",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        _createdAgentIds.Add(created.GetProperty("id").GetGuid());
        return created;
    }
}

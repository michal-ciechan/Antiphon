using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0007: <c>modelLevel</c> is a string-only wire. A numeric token used to bind as the enum
/// ordinal (0 → Frontier, 99 stored as a number) and an unknown name ("nope") used to 500
/// because ExceptionMiddleware only special-cased <c>HttpException</c>. These post raw JSON —
/// the bug is what happens before a typed C# request object exists.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AgentModelLevelBindTests
{
    private readonly AntiphonWebAppFactory _factory;
    private readonly List<Guid> _createdAgentIds = [];

    public AgentModelLevelBindTests(AntiphonWebAppFactory factory) => _factory = factory;

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
    public async Task Omitted_modelLevel_creates_High()
    {
        var (name, workingDirectory) = UniqueAgent();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/agents",
            JsonBody($$"""{"name":{{JsonEncode(name)}},"workingDirectory":{{JsonEncode(workingDirectory)}}}"""));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadObjectAsync(response);
        Track(created);
        created.GetProperty("modelLevel").GetString().ShouldBe("High");
    }

    [Test]
    public async Task String_Frontier_creates_Frontier()
    {
        var (name, workingDirectory) = UniqueAgent();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/agents",
            JsonBody($$"""
                {"name":{{JsonEncode(name)}},"workingDirectory":{{JsonEncode(workingDirectory)}},"modelLevel":"Frontier"}
                """));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadObjectAsync(response);
        Track(created);
        created.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
    }

    [Test]
    [Arguments("0")]
    [Arguments("99")]
    [Arguments("\"99\"")]
    [Arguments("\"nope\"")]
    public async Task Unbindable_modelLevel_is_400_and_creates_no_row(string modelLevelJson)
    {
        var (name, workingDirectory) = UniqueAgent();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/agents",
            JsonBody($$"""
                {"name":{{JsonEncode(name)}},"workingDirectory":{{JsonEncode(workingDirectory)}},"modelLevel":{{modelLevelJson}}}
                """));

        await ShouldBeBindFailureAsync(response);
        await AgentNamedShouldNotExistAsync(name);
    }

    [Test]
    public async Task Patch_numeric_0_is_400_and_leaves_stored_tier()
    {
        var created = await CreateWithModelLevelAsync("Frontier");
        var id = created.GetProperty("id").GetGuid();
        var name = created.GetProperty("name").GetString()!;
        var workingDirectory = created.GetProperty("workingDirectory").GetString()!;

        using var client = _factory.CreateClient();
        var patch = await client.PatchAsync(
            $"/api/agents/{id}",
            JsonBody($$"""
                {"name":{{JsonEncode(name)}},"workingDirectory":{{JsonEncode(workingDirectory)}},"details":"","assignmentPolicy":"AutoPick","modelLevel":0}
                """));

        await ShouldBeBindFailureAsync(patch);

        var stored = await client.GetFromJsonAsync<JsonElement>($"/api/agents/{id}");
        stored.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
    }

    [Test]
    public async Task Patch_omitted_modelLevel_leaves_stored_tier()
    {
        var created = await CreateWithModelLevelAsync("Frontier");
        var id = created.GetProperty("id").GetGuid();
        var name = created.GetProperty("name").GetString()!;
        var workingDirectory = created.GetProperty("workingDirectory").GetString()!;

        using var client = _factory.CreateClient();
        var patch = await client.PatchAsync(
            $"/api/agents/{id}",
            JsonBody($$"""
                {"name":{{JsonEncode(name)}},"workingDirectory":{{JsonEncode(workingDirectory)}},"details":"","assignmentPolicy":"AutoPick"}
                """));

        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ReadObjectAsync(patch);
        updated.GetProperty("modelLevel").GetString().ShouldBe("Frontier");
    }

    private async Task<JsonElement> CreateWithModelLevelAsync(string modelLevel)
    {
        var (name, workingDirectory) = UniqueAgent();
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            "/api/agents",
            JsonBody($$"""
                {"name":{{JsonEncode(name)}},"workingDirectory":{{JsonEncode(workingDirectory)}},"modelLevel":{{JsonEncode(modelLevel)}}}
                """));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadObjectAsync(response);
        Track(created);
        return created;
    }

    private void Track(JsonElement created) =>
        _createdAgentIds.Add(created.GetProperty("id").GetGuid());

    private async Task AgentNamedShouldNotExistAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Agents.AnyAsync(a => a.Name == name)).ShouldBeFalse();
    }

    private static async Task ShouldBeBindFailureAsync(HttpResponseMessage response)
    {
        ((int)response.StatusCode).ShouldBe(400);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("Bad Request");
        problem.GetProperty("status").GetInt32().ShouldBe(400);
        var detail = problem.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldNotBe("An unexpected error occurred.");
        detail.ShouldContain("modelLevel", Case.Insensitive);
    }

    private static (string Name, string WorkingDirectory) UniqueAgent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return ($"CARD-0007 {suffix}", $"D:/src/card-0007-{suffix}");
    }

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static string JsonEncode(string value) => JsonSerializer.Serialize(value);

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}

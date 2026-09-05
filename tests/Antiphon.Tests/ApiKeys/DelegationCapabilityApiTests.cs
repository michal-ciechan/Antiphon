using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0398 HTTP secrecy: raw JSON, not a DTO projection (CARD-0106 shape).
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class DelegationCapabilityApiTests
{
    private readonly AntiphonWebAppFactory _factory;

    public DelegationCapabilityApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task get_list_and_detail_keep_canary_and_hash_out_of_the_json()
    {
        using var client = _factory.CreateClient();
        using var root = new TempDir();
        var issued = await IssueAsync(client, root.Path);
        var canary = issued.Token;
        var hash = AgentTaskService.HashToken(canary);

        var list = await client.GetAsync("/api/delegation-capabilities");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listBody = await list.Content.ReadAsStringAsync();
        listBody.ShouldContain(issued.Name);
        listBody.ShouldNotContain(canary);
        listBody.ShouldNotContain(hash);
        HasTokenProperty(listBody).ShouldBeFalse();

        var detail = await client.GetAsync($"/api/delegation-capabilities/{issued.Id:D}");
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detailBody = await detail.Content.ReadAsStringAsync();
        detailBody.ShouldContain(issued.Name);
        detailBody.ShouldNotContain(canary);
        detailBody.ShouldNotContain(hash);
        HasTokenProperty(detailBody).ShouldBeFalse();
    }

    [Test]
    public async Task issue_and_rotate_http_include_token_revoke_and_gets_do_not()
    {
        using var client = _factory.CreateClient();
        using var root = new TempDir();
        var issued = await IssueAsync(client, root.Path);
        issued.Token.ShouldMatch(@"^[0-9a-f]{64}$");

        var rotate = await client.PostAsync($"/api/delegation-capabilities/{issued.Id:D}/rotate", null);
        rotate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotateJson = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync()).RootElement;
        rotateJson.GetProperty("token").GetString().ShouldNotBeNull().ShouldMatch(@"^[0-9a-f]{64}$");

        var revoke = await client.PostAsync($"/api/delegation-capabilities/{issued.Id:D}/revoke", null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revokeBody = await revoke.Content.ReadAsStringAsync();
        HasTokenProperty(revokeBody).ShouldBeFalse();

        var get = await client.GetAsync($"/api/delegation-capabilities/{issued.Id:D}");
        HasTokenProperty(await get.Content.ReadAsStringAsync()).ShouldBeFalse();
        var list = await client.GetAsync("/api/delegation-capabilities");
        HasTokenProperty(await list.Content.ReadAsStringAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task server_issue_does_not_write_LocalAppData()
    {
        using var client = _factory.CreateClient();
        using var root = new TempDir();
        var name = $"nowrite-{Guid.NewGuid():N}"[..20];
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Antiphon",
            "capabilities",
            $"{name}.dpapi");
        File.Exists(defaultPath).ShouldBeFalse();

        var response = await client.PostAsJsonAsync(
            "/api/delegation-capabilities",
            new { name, roots = new[] { root.Path } });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        File.Exists(defaultPath).ShouldBeFalse("the server must never write the CLI store");
    }

    [Test]
    public async Task attention_issue_info_revoke_warning_without_secret()
    {
        using var client = _factory.CreateClient();
        using var root = new TempDir();
        var issued = await IssueAsync(client, root.Path);
        var hash = AgentTaskService.HashToken(issued.Token);

        var afterIssue = await client.GetAsync("/api/attention");
        afterIssue.StatusCode.ShouldBe(HttpStatusCode.OK);
        var issueBody = await afterIssue.Content.ReadAsStringAsync();
        issueBody.ShouldContain(issued.Name);
        issueBody.ShouldContain("Info");
        issueBody.ShouldNotContain(issued.Token);
        issueBody.ShouldNotContain(hash);

        var revoke = await client.PostAsync($"/api/delegation-capabilities/{issued.Id:D}/revoke", null);
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterRevoke = await client.GetAsync("/api/attention");
        var revokeBody = await afterRevoke.Content.ReadAsStringAsync();
        revokeBody.ShouldContain("Warning");
        revokeBody.ShouldContain(issued.Name);
        revokeBody.ShouldNotContain(issued.Token);
        revokeBody.ShouldNotContain(hash);
    }

    private static async Task<Issued> IssueAsync(HttpClient client, string root)
    {
        var name = $"http-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync(
            "/api/delegation-capabilities",
            new { name, roots = new[] { root } });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return new Issued(
            json.GetProperty("id").GetGuid(),
            json.GetProperty("name").GetString()!,
            json.GetProperty("token").GetString()!);
    }

    private static bool HasTokenProperty(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ContainsTokenProperty(doc.RootElement);
    }

    private static bool ContainsTokenProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "token", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (ContainsTokenProperty(property.Value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsTokenProperty(item))
                    return true;
            }
        }

        return false;
    }

    private sealed record Issued(Guid Id, string Name, string Token);

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cap-api").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0167 S2: the per-kind provider endpoint setting over HTTP. A project carries one
/// endpoint per agent kind; an agent carries a mode (Inherit, Endpoint, Direct) and, in Endpoint
/// mode, its own endpoint. The setting is a URL plus a key NAME: no route here ever carries a key
/// value, and the one sentinel value this class writes to the key store is asserted absent from
/// every response it reads. Same host and schema as <see cref="ApiKeyApiTests"/>.
/// </summary>
[NotInParallel]
[ClassDataSource<ApiKeyApiWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class ProviderEndpointApiTests
{
    private const string Sentinel = "sentinel-0167-never-printed";

    private readonly ApiKeyApiWebAppFactory _factory;

    public ProviderEndpointApiTests(ApiKeyApiWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task C0167_project_endpoints_round_trip_per_kind_and_clear_with_empty_map()
    {
        using var client = _factory.CreateClient();
        var endpoints = new Dictionary<string, object>
        {
            ["ClaudeCode"] = new { baseUrl = "https://claude-proxy.test/", apiKeyName = "claude-key-0167" },
            ["Grok"] = new { baseUrl = "http://127.0.0.1:8787" },
            ["Codex"] = new { baseUrl = "https://codex-proxy.test/openai", apiKeyName = "codex-key-0167" },
        };

        var create = await client.PostAsJsonAsync("/api/projects", new
        {
            name = NewProjectName(),
            gitRepositoryUrl = "https://example.test/repo.git",
            baseBranch = "main",
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
            providerEndpoints = endpoints,
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadJsonAsync(create);
        var id = created.GetProperty("id").GetGuid();
        AssertThreeKinds(created);

        var fetched = await ReadJsonAsync(await client.GetAsync($"/api/projects/{id:D}"));
        AssertThreeKinds(fetched);
        fetched.GetRawText().ShouldNotContain("{{key:");

        // Omitting the map leaves it alone: an older client PUTting a project must not wipe it.
        var untouched = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(fetched, providerEndpoints: null));
        untouched.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertThreeKinds(await ReadJsonAsync(untouched));

        // The empty map is the explicit clear.
        var cleared = await client.PutAsJsonAsync(
            $"/api/projects/{id:D}", UpdateBody(fetched, providerEndpoints: new Dictionary<string, object>()));
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clearedBody = await ReadJsonAsync(cleared);
        clearedBody.GetProperty("providerEndpoints").ValueKind.ShouldBe(JsonValueKind.Object);
        clearedBody.GetProperty("providerEndpoints").EnumerateObject().Count().ShouldBe(0);
        var fetchedAgain = await ReadJsonAsync(await client.GetAsync($"/api/projects/{id:D}"));
        fetchedAgain.GetProperty("providerEndpoints").EnumerateObject().Count().ShouldBe(0);

        static void AssertThreeKinds(JsonElement project)
        {
            var map = project.GetProperty("providerEndpoints");
            map.ValueKind.ShouldBe(JsonValueKind.Object);
            map.EnumerateObject().Count().ShouldBe(3);
            var claude = map.GetProperty("ClaudeCode");
            claude.GetProperty("baseUrl").GetString().ShouldBe("https://claude-proxy.test", "the trailing slash is trimmed");
            claude.GetProperty("apiKeyName").GetString().ShouldBe("claude-key-0167");
            var grok = map.GetProperty("Grok");
            grok.GetProperty("baseUrl").GetString().ShouldBe("http://127.0.0.1:8787", "a loopback proxy is the normal case");
            grok.GetProperty("apiKeyName").ValueKind.ShouldBe(JsonValueKind.Null, "a key name is optional for Grok");
            var codex = map.GetProperty("Codex");
            codex.GetProperty("baseUrl").GetString().ShouldBe("https://codex-proxy.test/openai");
            codex.GetProperty("apiKeyName").GetString().ShouldBe("codex-key-0167");
        }
    }

    [Test]
    public async Task C0167_project_endpoint_422_on_unsupported_kind_and_bad_url()
    {
        using var client = _factory.CreateClient();
        var project = await CreateProjectAsync(client);
        var id = project.GetProperty("id").GetGuid();

        foreach (var kind in new[] { "OpenCode", "Raw", "Banana" })
        {
            var response = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
                providerEndpoints: new Dictionary<string, object> { [kind] = new { baseUrl = "https://proxy.test" } }));
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, $"kind {kind}");
            var problem = await ReadJsonAsync(response);
            problem.GetProperty("code").GetString().ShouldBe("provider_endpoint_unsupported_kind", $"kind {kind}");
            problem.GetProperty("errors").TryGetProperty($"providerEndpoints.{kind}", out _)
                .ShouldBeTrue($"the error names the offending kind ({kind})");
        }

        var badUrls = new[]
        {
            "https://user:pw-0167@proxy.test",   // userinfo
            "https://proxy.test/?tenant=1",      // query
            "https://proxy.test/#fragment",      // fragment
            "proxy.test/v1",                     // relative
            "ftp://proxy.test",                  // scheme
            "https://proxy.test/{{key:claude}}", // placeholder
            "https://proxy.test/" + new string('p', 2000), // length
        };
        foreach (var url in badUrls)
        {
            var response = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
                providerEndpoints: new Dictionary<string, object> { ["ClaudeCode"] = new { baseUrl = url } }));
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, url);
            var problem = await ReadJsonAsync(response);
            problem.GetProperty("code").GetString().ShouldBe("provider_endpoint_invalid_url", url);
            problem.GetProperty("errors").TryGetProperty("providerEndpoints.ClaudeCode.baseUrl", out _)
                .ShouldBeTrue($"the error names the field ({url})");
            problem.GetRawText().ShouldNotContain("pw-0167", Case.Sensitive,
                "a refused URL is never echoed; its userinfo could be a credential");
        }

        var badName = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
            providerEndpoints: new Dictionary<string, object>
            {
                ["ClaudeCode"] = new { baseUrl = "https://proxy.test", apiKeyName = "bad name" },
            }));
        badName.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(badName)).GetProperty("errors")
            .TryGetProperty("providerEndpoints.ClaudeCode.apiKeyName", out _).ShouldBeTrue();

        // Nothing refused was stored.
        var fetched = await ReadJsonAsync(await client.GetAsync($"/api/projects/{id:D}"));
        fetched.GetProperty("providerEndpoints").EnumerateObject().Count().ShouldBe(0);
    }

    [Test]
    public async Task C0167_save_refuses_the_urls_the_launch_refuses()
    {
        // Review e4b591eb D2: these five saved with 200 and were then refused at every launch by the
        // S1 validator (port zero; a quote, a backslash, U+00A0 or U+0085 in the path). The save path
        // now runs S1's rules after its own, under S1's codes; S2's pinned 422s above are untouched.
        using var client = _factory.CreateClient();
        var project = await CreateProjectAsync(client);
        var id = project.GetProperty("id").GetGuid();

        var refused = new (string Url, string Code)[]
        {
            ("http://127.0.0.1:0", "provider_endpoint_base_url_port"),
            ("https://proxy.example.com/v1\"x", "provider_endpoint_base_url_characters"),
            ("https://proxy.example.com/a\\b", "provider_endpoint_base_url_characters"),
            ("https://proxy.example.com/a\u00A0b", "provider_endpoint_base_url_characters"),
            ("https://proxy.example.com/a\u0085b", "provider_endpoint_base_url_characters"),
        };
        foreach (var (url, code) in refused)
        {
            var response = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
                providerEndpoints: new Dictionary<string, object> { ["ClaudeCode"] = new { baseUrl = url, apiKeyName = "claude-key-0167" } }));
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, url);
            var problem = await ReadJsonAsync(response);
            problem.GetProperty("code").GetString().ShouldBe(code, url);
            problem.GetProperty("errors").TryGetProperty("providerEndpoints.ClaudeCode.baseUrl", out _)
                .ShouldBeTrue($"the error names the field ({url})");
            problem.GetRawText().ShouldNotContain("proxy.example.com", Case.Sensitive, "a refused URL is never echoed");
        }

        // Nothing refused was stored, so no later launch can be refused for a URL the save accepted.
        var fetched = await ReadJsonAsync(await client.GetAsync($"/api/projects/{id:D}"));
        fetched.GetProperty("providerEndpoints").EnumerateObject().Count().ShouldBe(0);
    }

    [Test]
    public async Task C0167_project_endpoint_warns_on_unknown_key_name_and_accepts()
    {
        using var client = _factory.CreateClient();
        var project = await CreateProjectAsync(client);
        var id = project.GetProperty("id").GetGuid();
        var projectKey = $"missing-0167-{Guid.NewGuid():N}";
        var globalKey = $"global-0167-{Guid.NewGuid():N}";

        // D-8: an unknown name is accepted and warned about, because the key may be written later.
        var first = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
            providerEndpoints: new Dictionary<string, object>
            {
                ["ClaudeCode"] = new { baseUrl = "https://claude-proxy.test", apiKeyName = projectKey },
                ["Grok"] = new { baseUrl = "https://grok-proxy.test", apiKeyName = globalKey },
            }));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstBody = await ReadJsonAsync(first);
        var warnings = Warnings(firstBody);
        warnings.Count(w => w.Contains("provider_endpoint_key_not_found", StringComparison.Ordinal)).ShouldBe(2);
        warnings.ShouldContain(w => w.Contains(projectKey, StringComparison.Ordinal) && w.Contains("ClaudeCode", StringComparison.Ordinal));
        warnings.ShouldContain(w => w.Contains(globalKey, StringComparison.Ordinal) && w.Contains("Grok", StringComparison.Ordinal));
        firstBody.GetProperty("providerEndpoints").GetProperty("ClaudeCode").GetProperty("apiKeyName").GetString()
            .ShouldBe(projectKey, "the setting is stored despite the warning");

        // Writing the keys (one project-scoped, one global) silences the warnings; the value never comes back.
        (await client.PutAsJsonAsync($"/api/projects/{id:D}/api-keys/{projectKey}", new { value = Sentinel }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/api-keys/{globalKey}", new { value = Sentinel }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(firstBody, providerEndpoints: null));
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondBody = await ReadJsonAsync(second);
        Warnings(secondBody).ShouldNotContain(w => w.Contains("provider_endpoint_key_not_found", StringComparison.Ordinal));
        secondBody.GetRawText().ShouldNotContain(Sentinel, Case.Sensitive);
        (await (await client.GetAsync($"/api/projects/{id:D}")).Content.ReadAsStringAsync())
            .ShouldNotContain(Sentinel, Case.Sensitive);
    }

    [Test]
    public async Task C0167_agent_endpoint_mode_round_trips_and_endpoint_mode_requires_an_endpoint()
    {
        using var client = _factory.CreateClient();
        var created = await CreateAgentAsync(client);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("providerEndpointMode").GetString().ShouldBe("Inherit", "the default for every agent");
        created.GetProperty("providerEndpoint").ValueKind.ShouldBe(JsonValueKind.Null);

        var set = await client.PatchAsJsonAsync($"/api/agents/{id:D}", AgentBody(created,
            providerEndpointMode: "Endpoint",
            providerEndpoint: new { baseUrl = "https://agent-proxy.test/", apiKeyName = "agent-key-0167" }));
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setBody = await ReadJsonAsync(set);
        setBody.GetProperty("providerEndpointMode").GetString().ShouldBe("Endpoint");
        setBody.GetProperty("providerEndpoint").GetProperty("baseUrl").GetString().ShouldBe("https://agent-proxy.test");
        setBody.GetProperty("providerEndpoint").GetProperty("apiKeyName").GetString().ShouldBe("agent-key-0167");
        Warnings(setBody).ShouldContain(w => w.Contains("agent-key-0167", StringComparison.Ordinal),
            "the agent save warns about an unknown key name the same way the project save does");

        var fetched = await ReadJsonAsync(await client.GetAsync($"/api/agents/{id:D}"));
        fetched.GetProperty("providerEndpointMode").GetString().ShouldBe("Endpoint");
        fetched.GetProperty("providerEndpoint").GetProperty("baseUrl").GetString().ShouldBe("https://agent-proxy.test");

        // A PATCH that says nothing about the endpoint leaves it alone.
        var untouched = await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/agents/{id:D}", AgentBody(created)));
        untouched.GetProperty("providerEndpointMode").GetString().ShouldBe("Endpoint");
        untouched.GetProperty("providerEndpoint").GetProperty("apiKeyName").GetString().ShouldBe("agent-key-0167");

        // Direct exempts the agent from the project endpoint and drops the agent's own.
        var direct = await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/agents/{id:D}",
            AgentBody(created, providerEndpointMode: "Direct")));
        direct.GetProperty("providerEndpointMode").GetString().ShouldBe("Direct");
        direct.GetProperty("providerEndpoint").ValueKind.ShouldBe(JsonValueKind.Null);

        // Endpoint mode with nothing to point at is refused, not stored half-set.
        var missing = await client.PatchAsJsonAsync($"/api/agents/{id:D}", AgentBody(created, providerEndpointMode: "Endpoint"));
        missing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(missing)).GetProperty("code").GetString().ShouldBe("provider_endpoint_required");
        var stillDirect = await ReadJsonAsync(await client.GetAsync($"/api/agents/{id:D}"));
        stillDirect.GetProperty("providerEndpointMode").GetString().ShouldBe("Direct");

        // An endpoint beside a mode that cannot use it is a contradiction, refused rather than dropped.
        var contradiction = await client.PatchAsJsonAsync($"/api/agents/{id:D}", AgentBody(created,
            providerEndpointMode: "Inherit",
            providerEndpoint: new { baseUrl = "https://agent-proxy.test" }));
        contradiction.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(contradiction)).GetProperty("code").GetString().ShouldBe("provider_endpoint_not_applicable");

        var inherit = await ReadJsonAsync(await client.PatchAsJsonAsync($"/api/agents/{id:D}",
            AgentBody(created, providerEndpointMode: "Inherit")));
        inherit.GetProperty("providerEndpointMode").GetString().ShouldBe("Inherit");
        inherit.GetProperty("providerEndpoint").ValueKind.ShouldBe(JsonValueKind.Null);
        inherit.GetRawText().ShouldNotContain("{{key:");
    }

    [Test]
    public async Task C0167_codex_project_endpoint_without_key_name_is_422()
    {
        using var client = _factory.CreateClient();
        var project = await CreateProjectAsync(client);
        var id = project.GetProperty("id").GetGuid();

        var refused = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
            providerEndpoints: new Dictionary<string, object> { ["Codex"] = new { baseUrl = "https://codex-proxy.test" } }));
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadJsonAsync(refused);
        problem.GetProperty("code").GetString().ShouldBe("provider_endpoint_key_required");
        problem.GetProperty("errors").TryGetProperty("providerEndpoints.Codex.apiKeyName", out _).ShouldBeTrue();

        var accepted = await client.PutAsJsonAsync($"/api/projects/{id:D}", UpdateBody(project,
            providerEndpoints: new Dictionary<string, object>
            {
                ["Codex"] = new { baseUrl = "https://codex-proxy.test", apiKeyName = "codex-key-0167" },
            }));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var codex = (await ReadJsonAsync(accepted)).GetProperty("providerEndpoints").GetProperty("Codex");
        codex.GetProperty("baseUrl").GetString().ShouldBe("https://codex-proxy.test",
            "the /v1 suffix is the projection's business (S3), not the stored setting's");
        codex.GetProperty("apiKeyName").GetString().ShouldBe("codex-key-0167");

        // The same rule on the agent, checked against the kind the PATCH itself sets.
        var agent = await CreateAgentAsync(client);
        var agentId = agent.GetProperty("id").GetGuid();
        var agentRefused = await client.PatchAsJsonAsync($"/api/agents/{agentId:D}", AgentBody(agent,
            kind: "Codex",
            providerEndpointMode: "Endpoint",
            providerEndpoint: new { baseUrl = "https://codex-proxy.test" }));
        agentRefused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(agentRefused)).GetProperty("code").GetString().ShouldBe("provider_endpoint_key_required");
        var unchanged = await ReadJsonAsync(await client.GetAsync($"/api/agents/{agentId:D}"));
        unchanged.GetProperty("providerEndpointMode").GetString().ShouldBe("Inherit");
        unchanged.GetProperty("kind").GetString().ShouldBe("ClaudeCode", "a refused PATCH writes nothing");
    }

    private static string NewProjectName() => $"Provider endpoint {Guid.NewGuid():N}";

    private static async Task<JsonElement> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            name = NewProjectName(),
            gitRepositoryUrl = "https://example.test/repo.git",
            baseBranch = "main",
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> CreateAgentAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync("/api/agents", new
        {
            name = $"CARD-0167 {suffix}",
            workingDirectory = $"D:/src/card-0167-{suffix}",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    /// <summary>A full PUT body from a project's own JSON, with the endpoint map as given (null omits it).</summary>
    private static Dictionary<string, object?> UpdateBody(JsonElement project, Dictionary<string, object>? providerEndpoints)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = project.GetProperty("name").GetString(),
            ["gitRepositoryUrl"] = project.GetProperty("gitRepositoryUrl").GetString(),
            ["constitutionPath"] = project.GetProperty("constitutionPath").GetString(),
            ["gitHubIntegrationEnabled"] = project.GetProperty("gitHubIntegrationEnabled").GetBoolean(),
            ["notificationsEnabled"] = project.GetProperty("notificationsEnabled").GetBoolean(),
            ["baseBranch"] = project.GetProperty("baseBranch").GetString(),
        };
        if (providerEndpoints is not null)
            body["providerEndpoints"] = providerEndpoints;
        return body;
    }

    /// <summary>A PATCH body carrying the agent's required fields plus whatever the test sets.</summary>
    private static Dictionary<string, object?> AgentBody(
        JsonElement agent, string? providerEndpointMode = null, object? providerEndpoint = null, string? kind = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = agent.GetProperty("name").GetString(),
            ["workingDirectory"] = agent.GetProperty("workingDirectory").GetString(),
            ["assignmentPolicy"] = agent.GetProperty("assignmentPolicy").GetString(),
        };
        if (providerEndpointMode is not null)
            body["providerEndpointMode"] = providerEndpointMode;
        if (providerEndpoint is not null)
            body["providerEndpoint"] = providerEndpoint;
        if (kind is not null)
            body["kind"] = kind;
        return body;
    }

    private static List<string> Warnings(JsonElement dto) =>
        dto.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array
            ? warnings.EnumerateArray().Select(w => w.GetString() ?? string.Empty).ToList()
            : [];

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

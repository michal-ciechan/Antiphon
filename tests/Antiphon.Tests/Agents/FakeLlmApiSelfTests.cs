using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.AgentTui;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Un-gated FakeLlmApi self-tests (CARD-0168 S1). No real CLI is spawned — CI-safe.
/// </summary>
[Category("Unit")]
public class FakeLlmApiSelfTests
{
    [Test]
    public async Task WaitForAsync_returns_matching_request_and_times_out_otherwise()
    {
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });

        using var client = new HttpClient { BaseAddress = new Uri(stub.BaseUrl) };
        _ = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/hello"));

        var hit = await stub.Requests.WaitForAsync(
            r => r.Method == "HEAD" && r.Path == "/api/hello",
            TimeSpan.FromSeconds(5));
        hit.ShouldNotBeNull();
        hit!.ListenPort.ShouldBe(stub.ListenPort);

        var miss = await stub.Requests.WaitForAsync(
            r => r.Path == "/never",
            TimeSpan.FromMilliseconds(200));
        miss.ShouldBeNull();
    }

    [Test]
    public async Task Recording_happens_before_routing_decision_including_404()
    {
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });

        using var client = new HttpClient { BaseAddress = new Uri(stub.BaseUrl) };
        var response = await client.GetAsync("/no/such/path?x=1");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var recorded = await stub.Requests.WaitForAsync(
            r => r.Path == "/no/such/path",
            TimeSpan.FromSeconds(2));
        recorded.ShouldNotBeNull();
        recorded!.QueryString.ShouldBe("?x=1");
        recorded.Method.ShouldBe("GET");
        recorded.Body.ShouldBe("");
    }

    [Test]
    public async Task Claude_messages_records_x_api_key_and_full_body_before_scripted_error()
    {
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        stub.Script.Enqueue(StubEndpointKeys.ClaudeMessages, new ScriptedError(400));

        using var client = new HttpClient { BaseAddress = new Uri(stub.BaseUrl) };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages?beta=true");
        req.Headers.TryAddWithoutValidation("x-api-key", "stub-claude-key");
        req.Content = new StringContent("""{"stream":true,"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(req);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var recorded = await stub.Requests.WaitForAsync(
            r => r.Path == "/v1/messages" && r.Body.Contains("hi", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));
        recorded.ShouldNotBeNull();
        recorded!.Headers.ShouldContainKey("x-api-key");
        recorded.Headers["x-api-key"].ShouldBe(["stub-claude-key"]);
        recorded.QueryString.ShouldBe("?beta=true");
        recorded.BodyByteLength.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Claude_scripted_text_turn_emits_anthropic_sse()
    {
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        stub.Script.Enqueue(StubEndpointKeys.ClaudeMessages, new ScriptedTextTurn("hello-from-stub"));

        using var client = new HttpClient { BaseAddress = new Uri(stub.BaseUrl) };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.SendAsync(req);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("event: message_start");
        body.ShouldContain("event: content_block_delta");
        body.ShouldContain("hello-from-stub");
        body.ShouldContain("event: message_stop");
    }

    [Test]
    public async Task Sidecar_truncates_body_and_redacts_Authorization()
    {
        var jsonl = Path.Combine(Path.GetTempPath(), $"fakellm-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions
            {
                Codex = true,
                JsonlPath = jsonl,
            });

            var big = new string('X', RecordedRequestStore.SidecarBodyTruncateBytes + 2048);
            using var client = new HttpClient { BaseAddress = new Uri(stub.BaseUrl) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "real-looking-token");
            req.Content = new StringContent(big, Encoding.UTF8, "application/json");
            _ = await client.SendAsync(req);

            await stub.Requests.WaitForAsync(r => r.Path == "/v1/responses", TimeSpan.FromSeconds(2));

            // Memory keeps the full body + raw Authorization.
            var mem = stub.Requests.All.ShouldHaveSingleItem();
            mem.Body.Length.ShouldBe(big.Length);
            mem.Headers["Authorization"].ShouldBe(["Bearer real-looking-token"]);

            File.Exists(jsonl).ShouldBeTrue();
            var line = (await File.ReadAllTextAsync(jsonl)).Trim();
            line.ShouldContain("BodyTruncated\":true");
            line.ShouldContain("sha256:");
            line.ShouldNotContain("real-looking-token");
            // Full body must not appear in the sidecar.
            line.ShouldNotContain(big);
            var expectedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(big)))
                .ToLowerInvariant();
            line.ShouldContain(expectedSha);
        }
        finally
        {
            if (File.Exists(jsonl)) File.Delete(jsonl);
        }
    }

    [Test]
    public async Task Reset_clears_store()
    {
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        using var client = new HttpClient { BaseAddress = new Uri(stub.BaseUrl) };
        _ = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/hello"));
        stub.Requests.All.Count.ShouldBe(1);
        stub.Requests.Reset().ShouldBe(1);
        stub.Requests.All.ShouldBeEmpty();
    }

    [Test]
    public void ForClaude_sets_base_url_key_config_dir_and_defaults()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        try
        {
            var overlay = RealCliStubEnv.ForClaude("http://127.0.0.1:9/", "stub-claude-abc", configDir);
            overlay.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://127.0.0.1:9");
            overlay.Env["ANTHROPIC_API_KEY"].ShouldBe("stub-claude-abc");
            overlay.Env["CLAUDE_CONFIG_DIR"].ShouldBe(Path.GetFullPath(configDir));
            overlay.Env["DISABLE_AUTOUPDATER"].ShouldBe("1");
            overlay.Env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"].ShouldBe("1");
            overlay.Env["CLAUDECODE"].ShouldBe("");
            overlay.Args.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    [Test]
    public void ForGrok_requires_chat_proxy_var_and_sets_both_base_urls()
    {
        var overlay = RealCliStubEnv.ForGrok("http://127.0.0.1:9", "stub-grok-key");
        overlay.Env.ShouldContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
        overlay.Env["GROK_CLI_CHAT_PROXY_BASE_URL"].ShouldBe("http://127.0.0.1:9");
        overlay.Env["GROK_XAI_API_BASE_URL"].ShouldBe("http://127.0.0.1:9");
        overlay.Env["GROK_CODE_XAI_API_KEY"].ShouldBe("stub-grok-key");
        overlay.Env["GROK_TELEMETRY_ENABLED"].ShouldBe("0");
        overlay.Env.ShouldNotContainKey("GROK_AUTH_PATH");
        overlay.Args.ShouldBeEmpty();
    }

    [Test]
    public void ForCodex_emits_five_c_args_and_openai_api_key()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), $"codex-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(codexHome);
        try
        {
            var overlay = RealCliStubEnv.ForCodex("http://127.0.0.1:9", "stub-codex-key", codexHome);
            overlay.Env["OPENAI_API_KEY"].ShouldBe("stub-codex-key");
            overlay.Env["CODEX_HOME"].ShouldBe(Path.GetFullPath(codexHome));
            overlay.Args.Count.ShouldBe(10); // five (-c, value) pairs
            var values = overlay.Args.Where((_, i) => i % 2 == 1).ToArray();
            values[0].ShouldBe("model_providers.stub.name=\"Stub\"");
            values[1].ShouldBe("model_providers.stub.base_url=\"http://127.0.0.1:9/v1\"");
            values[2].ShouldBe("model_providers.stub.env_key=\"OPENAI_API_KEY\"");
            values[3].ShouldBe("model_providers.stub.wire_api=\"responses\"");
            values[4].ShouldBe("model_provider=stub");
        }
        finally
        {
            try { Directory.Delete(codexHome, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void HeadedCodexGate_launch_env_uses_the_dedicated_test_home()
    {
        var env = HeadedCodexGate.RealServiceEnv();

        env.ShouldContainKey("CODEX_HOME");
        env["CODEX_HOME"].ShouldBe(HeadedCodexGate.TestHome);
        env["CODEX_HOME"].ShouldNotBe(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
    }

    [Test]
    public void ForClaude_launch_override_survives_resolver_merge_order()
    {
        // CARD-0168 S4 cheap B-agent layer-order pin. Same merge loops as AgentTuiLaunchResolver
        // (profile/definition -> project default -> agent env -> LaunchEnvOverride -> ExtraEnv).
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        try
        {
            var overlay = RealCliStubEnv.ForClaude("http://127.0.0.1:9", "stub-claude-key", configDir);
            var registry = new AgentRegistry(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "claude",
                Definitions =
                {
                    ["claude"] = new AgentDefinition
                    {
                        Kind = nameof(AgentKind.ClaudeCode),
                        Exe = "claude.exe",
                        Env = new Dictionary<string, string> { ["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com" },
                    }
                },
            }));

            var spec = registry.Resolve("claude", new AgentLaunchOptions(
                Cwd: "C:\\tmp",
                AgentEnv: new Dictionary<string, string>
                {
                    ["ANTHROPIC_BASE_URL"] = "https://from-agent.example",
                    ["ANTHROPIC_API_KEY"] = "from-agent",
                },
                ExtraEnv: new Dictionary<string, string>
                {
                    ["ANTIPHON_SESSION_ID"] = "the-real-session",
                },
                LaunchEnvOverride: overlay.Env));

            spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://127.0.0.1:9",
                "LaunchEnvOverride carrying ForClaude must beat agent env and the definition default");
            spec.Env["ANTHROPIC_API_KEY"].ShouldBe("stub-claude-key");
            spec.Env["CLAUDE_CONFIG_DIR"].ShouldBe(Path.GetFullPath(configDir));
            spec.Env["ANTIPHON_SESSION_ID"].ShouldBe("the-real-session",
                "ExtraEnv (ANTIPHON_* plumbing) still outranks the override");
        }
        finally
        {
            try { Directory.Delete(configDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Test]
    public async Task Ephemeral_ports_differ_across_instances()
    {
        await using var a = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        await using var b = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        a.ListenPort.ShouldNotBe(b.ListenPort);
        a.BaseUrl.ShouldNotBe(b.BaseUrl);
    }
}

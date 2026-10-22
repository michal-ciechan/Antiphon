using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0167 S1 — the provider-endpoint value object and its pure per-kind projection.
///
/// <para>Custody: these tests hold key NAMES only. The projection never takes a value, so nothing here
/// constructs one; the only value-shaped strings are the synthetic <c>stub-*</c> tokens
/// <see cref="RealCliStubEnv"/> requires, and the cross-check asserts they never reach the
/// projection's output.</para>
/// </summary>
[Category("Unit")]
public class ProviderEndpointProjectionTests
{
    private const string KeyName = "c0167-proxy-key";
    private const string Placeholder = "{{key:" + KeyName + "}}";

    [Test]
    public void C0167_Claude_projects_base_url_and_key_placeholder()
    {
        var projected = ProviderEndpointProjection.For(
            AgentKind.ClaudeCode, new ProviderEndpoint("https://proxy.local:8443/", KeyName));

        projected.Env.Count.ShouldBe(2);
        projected.Env["ANTHROPIC_BASE_URL"].ShouldBe("https://proxy.local:8443", "trailing slash trimmed");
        projected.Env["ANTHROPIC_API_KEY"].ShouldBe(Placeholder);
        projected.Args.ShouldBeEmpty();

        // The OAuth store is the CLI's own; the projection never names it (plan: what must not change).
        projected.Env.ShouldNotContainKey("CLAUDE_CONFIG_DIR");

        // A key-injecting proxy needs only the URL: no name, no key variable at all.
        var urlOnly = ProviderEndpointProjection.For(
            AgentKind.ClaudeCode, new ProviderEndpoint("https://proxy.local:8443", null));
        urlOnly.Env.Count.ShouldBe(1);
        urlOnly.Env["ANTHROPIC_BASE_URL"].ShouldBe("https://proxy.local:8443");
        urlOnly.Env.ShouldNotContainKey("ANTHROPIC_API_KEY");
    }

    [Test]
    public void C0167_Grok_projects_both_base_urls_and_chat_var_is_present()
    {
        var projected = ProviderEndpointProjection.For(
            AgentKind.Grok, new ProviderEndpoint("http://127.0.0.1:9/", KeyName));

        // The chat redirect is mandatory: GROK_XAI_API_BASE_URL alone redirects only GET /api-key and
        // lets every completion hit real xAI (CARD-0168 decision 4, D-6).
        projected.Env.ShouldContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
        projected.Env["GROK_CLI_CHAT_PROXY_BASE_URL"].ShouldBe("http://127.0.0.1:9");
        projected.Env["GROK_XAI_API_BASE_URL"].ShouldBe("http://127.0.0.1:9");
        projected.Env["GROK_CODE_XAI_API_KEY"].ShouldBe(Placeholder);
        projected.Env.Count.ShouldBe(3);
        projected.Args.ShouldBeEmpty();

        // Never the OAuth store, never the gkp wrapper's own variable.
        projected.Env.ShouldNotContainKey("GROK_AUTH_PATH");
        projected.Env.ShouldNotContainKey("GROK_BASE_URL");

        var urlOnly = ProviderEndpointProjection.For(
            AgentKind.Grok, new ProviderEndpoint("http://127.0.0.1:9", null));
        urlOnly.Env.Count.ShouldBe(2);
        urlOnly.Env.ShouldContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
        urlOnly.Env.ShouldContainKey("GROK_XAI_API_BASE_URL");
        urlOnly.Env.ShouldNotContainKey("GROK_CODE_XAI_API_KEY");
    }

    [Test]
    public void C0167_Codex_projects_openai_key_and_five_args_ending_v1()
    {
        var projected = ProviderEndpointProjection.For(
            AgentKind.Codex, new ProviderEndpoint("https://proxy.local/openai/", KeyName));

        // The key is always OPENAI_API_KEY (D-5) so PhoneHomeLaunchPolicy's refusal stays effective.
        projected.Env.Count.ShouldBe(1);
        projected.Env["OPENAI_API_KEY"].ShouldBe(Placeholder);
        projected.Env.ShouldNotContainKey("CODEX_HOME");
        projected.Env.ShouldNotContainKey("OPENAI_BASE_URL");

        projected.Args.Count.ShouldBe(10, "five (-c, value) pairs");
        projected.Args.Where((_, i) => i % 2 == 0).ShouldAllBe(a => a == CodexLaunchArgs.ConfigFlag);
        var values = projected.Args.Where((_, i) => i % 2 == 1).ToArray();
        values[0].ShouldBe("model_providers.antiphon.name=\"Antiphon endpoint\"");
        values[1].ShouldBe("model_providers.antiphon.base_url=\"https://proxy.local/openai/v1\"");
        values[2].ShouldBe("model_providers.antiphon.env_key=\"OPENAI_API_KEY\"");
        values[3].ShouldBe("model_providers.antiphon.wire_api=\"responses\"");
        values[4].ShouldBe("model_provider=antiphon");

        // /v1 is appended only when absent, matched case-insensitively like the stub builder.
        var alreadyV1 = ProviderEndpointProjection.For(
            AgentKind.Codex, new ProviderEndpoint("https://proxy.local/V1/", KeyName));
        alreadyV1.Args[3].ShouldBe("model_providers.antiphon.base_url=\"https://proxy.local/V1\"");

        // The placeholder lives in an env VALUE only; the argv tripwire passes over every argument.
        for (var i = 0; i < projected.Args.Count; i++)
            ApiKeyPlaceholder.EnsureAbsent(projected.Args[i], $"projected Codex argument {i}");

        // A production redirect and a stub canary never share a provider table entry (D-3).
        ProviderEndpointProjection.CodexProviderId.ShouldBe("antiphon");
        ProviderEndpointProjection.CodexProviderId.ShouldNotBe("stub");
    }

    [Test]
    public void C0167_Codex_requires_a_key_name()
    {
        var endpoint = new ProviderEndpoint("https://proxy.local", null);

        // Save path: 422 naming the field and the rule.
        var save = Should.Throw<ValidationException>(() => endpoint.Validate(AgentKind.Codex));
        save.StatusCode.ShouldBe(422);
        save.Code.ShouldBe("provider_endpoint_key_required");
        save.Errors.Keys.ShouldBe(["providerEndpoint.apiKeyName"]);

        // Resolve path: the projection refuses too, so a malformed stored row cannot produce an
        // env_key that names an unset variable.
        var resolve = Should.Throw<ConflictException>(
            () => ProviderEndpointProjection.For(AgentKind.Codex, endpoint));
        resolve.StatusCode.ShouldBe(409);
        resolve.Code.ShouldBe("provider_endpoint_key_required");

        // Claude and Grok accept a URL-only endpoint; whitespace is "no name", not a name.
        endpoint.Validate(AgentKind.ClaudeCode).ApiKeyName.ShouldBeNull();
        endpoint.Validate(AgentKind.Grok).ApiKeyName.ShouldBeNull();
        new ProviderEndpoint("https://proxy.local", "   ").Validate(AgentKind.ClaudeCode)
            .ApiKeyName.ShouldBeNull();

        // With a name, Codex validates and the name is trimmed and kept verbatim (Ordinal).
        new ProviderEndpoint("https://proxy.local", $" {KeyName} ").Validate(AgentKind.Codex)
            .ApiKeyName.ShouldBe(KeyName);
    }

    [Test]
    [Arguments(AgentKind.OpenCode)]
    [Arguments(AgentKind.Raw)]
    public void C0167_Unsupported_kind_refuses_by_code(AgentKind kind)
    {
        var endpoint = new ProviderEndpoint("https://proxy.local", KeyName);

        var resolve = Should.Throw<ConflictException>(() => ProviderEndpointProjection.For(kind, endpoint));
        resolve.StatusCode.ShouldBe(409);
        resolve.Code.ShouldBe("provider_endpoint_unsupported_kind");
        resolve.Message.ShouldContain(kind.ToString());

        var save = Should.Throw<ValidationException>(() => endpoint.Validate(kind));
        save.StatusCode.ShouldBe(422);
        save.Code.ShouldBe("provider_endpoint_unsupported_kind");

        ProviderEndpoint.IsSupportedKind(kind).ShouldBeFalse();

        // Describe answers "nothing" for a kind the setting does not apply to; it never throws, so a
        // catalogue listing every kind can call it uniformly (S4: Supported=false).
        var described = ProviderEndpointProjection.Describe(kind);
        described.Env.ShouldBeEmpty();
        described.Args.ShouldBeEmpty();
    }

    [Test]
    [Arguments("https://user:pw@proxy.local/", "provider_endpoint_base_url_userinfo")]
    [Arguments("https://proxy.local/v1?tenant=1", "provider_endpoint_base_url_query")]
    [Arguments("https://proxy.local/#frag", "provider_endpoint_base_url_fragment")]
    [Arguments("proxy.local/v1", "provider_endpoint_base_url_relative")]
    [Arguments("https://proxy.local/" + Placeholder, "provider_endpoint_placeholder")]
    public void C0167_Validate_refuses_userinfo_query_fragment_relative_and_placeholder(
        string baseUrl, string expectedCode)
    {
        var ex = Should.Throw<ValidationException>(
            () => new ProviderEndpoint(baseUrl, KeyName).Validate(AgentKind.ClaudeCode));

        ex.StatusCode.ShouldBe(422);
        ex.Code.ShouldBe(expectedCode);
        ex.Errors.Keys.ShouldBe(["providerEndpoint.baseUrl"]);

        // The refusal names the rule, never the URL: a path or query could carry a tenant token.
        var text = string.Join(" ", ex.Errors.Values.SelectMany(v => v));
        text.ShouldNotBeNullOrWhiteSpace();
        text.ShouldNotContain(baseUrl);

        // The same refusals apply when no kind is known yet (the agent path validates before the
        // kind is resolved), and the field prefix follows the caller's field name.
        var unscoped = Should.Throw<ValidationException>(
            () => new ProviderEndpoint(baseUrl, KeyName).Validate(kind: null, field: "providerEndpoints.ClaudeCode"));
        unscoped.Code.ShouldBe(expectedCode);
        unscoped.Errors.Keys.ShouldBe(["providerEndpoints.ClaudeCode.baseUrl"]);
    }

    [Test]
    public void C0167_Projection_names_match_RealCliStubEnv()
    {
        // RealCliStubEnv stays the only sanctioned TEST builder; the projection is its production
        // counterpart. The two must name the same variables and the same Codex -c keys, so a stub
        // canary that passes says something about a production redirect.
        string[] stubOnly =
        [
            // Isolation the stub adds and the projection must never set.
            "CLAUDE_CONFIG_DIR", "CODEX_HOME",
            // Kind defaults the resolver applies itself (ApplyClaudeEnvironmentDefaults / Grok).
            "DISABLE_AUTOUPDATER", "CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN",
            "GROK_TELEMETRY_ENABLED", "GROK_FEEDBACK_ENABLED",
            // Claude nesting markers the stub empties for a canary under an Antiphon session.
            "CLAUDECODE", "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID",
            "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDE_CODE_ENTRYPOINT",
        ];

        var endpoint = new ProviderEndpoint("http://127.0.0.1:9", KeyName);
        var scratch = Path.Combine(Path.GetTempPath(), $"c0167-names-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            var claudeStub = RealCliStubEnv.ForClaude("http://127.0.0.1:9", "stub-claude-key", scratch);
            var grokStub = RealCliStubEnv.ForGrok("http://127.0.0.1:9", "stub-grok-key");
            var codexStub = RealCliStubEnv.ForCodex(
                "http://127.0.0.1:9", "stub-codex-key", Path.Combine(scratch, "codex-home"));

            var claude = ProviderEndpointProjection.For(AgentKind.ClaudeCode, endpoint);
            var grok = ProviderEndpointProjection.For(AgentKind.Grok, endpoint);
            var codex = ProviderEndpointProjection.For(AgentKind.Codex, endpoint);

            Names(claude.Env).ShouldBe(Names(claudeStub.Env).Except(stubOnly, StringComparer.Ordinal), ignoreOrder: true);
            Names(grok.Env).ShouldBe(Names(grokStub.Env).Except(stubOnly, StringComparer.Ordinal), ignoreOrder: true);
            Names(codex.Env).ShouldBe(Names(codexStub.Env).Except(stubOnly, StringComparer.Ordinal), ignoreOrder: true);

            // Codex -c key set with the provider id substituted; flags in the same even positions.
            var stubKeys = ArgKeys(codexStub.Args)
                .Select(k => k.Replace("model_providers.stub.", $"model_providers.{ProviderEndpointProjection.CodexProviderId}.", StringComparison.Ordinal))
                .ToArray();
            ArgKeys(codex.Args).ShouldBe(stubKeys);
            codex.Args.Where((_, i) => i % 2 == 0).ShouldAllBe(a => a == CodexLaunchArgs.ConfigFlag);
            // env_key and wire_api are the parts of the provider table that must be identical.
            ArgValue(codex.Args, "env_key").ShouldBe(ArgValue(codexStub.Args, "env_key"));
            ArgValue(codex.Args, "wire_api").ShouldBe(ArgValue(codexStub.Args, "wire_api"));
            ArgValue(codex.Args, "model_provider").ShouldBe(ProviderEndpointProjection.CodexProviderId);

            // The tripwire passes over every projected argument, for every supported kind.
            foreach (var projected in new[] { claude, grok, codex })
            {
                for (var i = 0; i < projected.Args.Count; i++)
                    ApiKeyPlaceholder.EnsureAbsent(projected.Args[i], $"projected argument {i}");
            }

            // Describe mirrors For: same names, same argument count, literal placeholders only.
            foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex })
            {
                var forKind = ProviderEndpointProjection.For(kind, endpoint);
                var described = ProviderEndpointProjection.Describe(kind);
                Names(described.Env).ShouldBe(Names(forKind.Env), ignoreOrder: true);
                described.Args.Count.ShouldBe(forKind.Args.Count);
                described.Env.Values.ShouldAllBe(v => v == "<url>" || v == "{{key:<name>}}");
                string.Join("\n", described.Args).ShouldNotContain(KeyName);
            }

            // The projection took no value, so the stub's synthetic tokens cannot be in its output.
            var everything = string.Join("\n",
                new[] { claude, grok, codex }.SelectMany(p => p.Env.Values.Concat(p.Args)));
            everything.ShouldNotContain("stub-");
            everything.ShouldContain(Placeholder);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IEnumerable<string> Names(IReadOnlyDictionary<string, string> env) => env.Keys;

    private static IEnumerable<string> ArgKeys(IReadOnlyList<string> args) =>
        args.Where((_, i) => i % 2 == 1).Select(v => v.Split('=', 2)[0]);

    private static string ArgValue(IReadOnlyList<string> args, string keySuffix) =>
        args.Where((_, i) => i % 2 == 1)
            .Single(v => v.Split('=', 2)[0].EndsWith(keySuffix, StringComparison.Ordinal))
            .Split('=', 2)[1];
}

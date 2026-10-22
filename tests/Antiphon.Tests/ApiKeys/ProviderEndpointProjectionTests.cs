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

    // ---- Carried from the S1 Final Review (task 149377a0): the injection and scheme witnesses ------

    private static readonly IReadOnlyDictionary<string, string> RefusedUrls = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["quote"] = "https://proxy.local/a\"b",            // U+0022 would close the Codex -c TOML string
        ["backslash"] = "https://proxy.local/a\\b",        // a backslash starts a TOML escape
        ["space"] = "https://proxy.local/a b",
        ["tab"] = "https://proxy.local/a\tb",
        ["newline"] = "https://proxy.local/a\nb",
        ["carriage-return"] = "https://proxy.local/a\rb",
        ["del-control"] = "https://proxy.local/a\u007Fb",
        ["c1-control"] = "https://proxy.local/a\u0085b",
        ["line-separator-2028"] = "https://proxy.local/a\u2028b",
        ["paragraph-separator-2029"] = "https://proxy.local/a\u2029b",
        ["ideographic-space"] = "https://proxy.local/a\u3000b",
        ["ftp"] = "ftp://proxy.local/v1",
        ["ws"] = "ws://proxy.local/v1",
        ["scheme-relative"] = "//proxy.local/v1",
    };

    [Test]
    [Arguments("quote", "provider_endpoint_base_url_characters")]
    [Arguments("backslash", "provider_endpoint_base_url_characters")]
    [Arguments("space", "provider_endpoint_base_url_characters")]
    [Arguments("tab", "provider_endpoint_base_url_characters")]
    [Arguments("newline", "provider_endpoint_base_url_characters")]
    [Arguments("carriage-return", "provider_endpoint_base_url_characters")]
    [Arguments("del-control", "provider_endpoint_base_url_characters")]
    [Arguments("c1-control", "provider_endpoint_base_url_characters")]
    [Arguments("line-separator-2028", "provider_endpoint_base_url_characters")]
    [Arguments("paragraph-separator-2029", "provider_endpoint_base_url_characters")]
    [Arguments("ideographic-space", "provider_endpoint_base_url_characters")]
    [Arguments("ftp", "provider_endpoint_base_url_scheme")]
    [Arguments("ws", "provider_endpoint_base_url_scheme")]
    [Arguments("scheme-relative", "provider_endpoint_base_url_scheme")]
    public void C0167_Validate_refuses_characters_and_non_http_schemes(string caseName, string expectedCode)
    {
        var baseUrl = RefusedUrls[caseName];

        // Save path: 422 naming the field and the rule, never the URL.
        var saved = Should.Throw<ValidationException>(
            () => new ProviderEndpoint(baseUrl, KeyName).Validate(AgentKind.Codex));
        saved.StatusCode.ShouldBe(422);
        saved.Code.ShouldBe(expectedCode);
        saved.Errors.Keys.ShouldBe(["providerEndpoint.baseUrl"]);
        string.Join(" ", saved.Errors.Values.SelectMany(v => v)).ShouldNotContain(baseUrl);

        // Resolve path: the same row written around the API is refused by the projection before any
        // argument exists, so nothing of it can reach a -c TOML string or an env value.
        var atLaunch = Should.Throw<ValidationException>(
            () => ProviderEndpointProjection.For(AgentKind.Codex, new ProviderEndpoint(baseUrl, KeyName)));
        atLaunch.Code.ShouldBe(expectedCode);
    }

    [Test]
    [Arguments("https://proxy.local:0", "provider_endpoint_base_url_port")]
    [Arguments("https://proxy.local:65536/v1", "provider_endpoint_base_url_port")]
    [Arguments("http://127.0.0.1:99999", "provider_endpoint_base_url_port")]
    [Arguments("http://[::1]:0/", "provider_endpoint_base_url_port")]
    public void C0167_Validate_reports_port_zero_and_out_of_range_by_code(string baseUrl, string expectedCode)
    {
        var ex = Should.Throw<ValidationException>(
            () => new ProviderEndpoint(baseUrl, KeyName).Validate(AgentKind.ClaudeCode));
        ex.StatusCode.ShouldBe(422);
        ex.Code.ShouldBe(expectedCode);
        ex.Errors.Keys.ShouldBe(["providerEndpoint.baseUrl"]);
        string.Join(" ", ex.Errors.Values.SelectMany(v => v)).ShouldNotContain(baseUrl);

        // In-range ports, explicit or implied, are kept verbatim, IPv6 literals included.
        new ProviderEndpoint("https://proxy.local:8443/", KeyName).Validate(AgentKind.ClaudeCode).BaseUrl
            .ShouldBe("https://proxy.local:8443");
        new ProviderEndpoint("http://[::1]:8080/", KeyName).Validate(AgentKind.ClaudeCode).BaseUrl
            .ShouldBe("http://[::1]:8080");
        new ProviderEndpoint("https://proxy.local:65535", KeyName).Validate(AgentKind.ClaudeCode).BaseUrl
            .ShouldBe("https://proxy.local:65535");
    }

    [Test]
    public void C0167_For_refuses_a_stored_bad_url_without_echoing_it()
    {
        // Parse is not a gate (an unreadable column must not take an agent offline), so a row written
        // around the API survives it; For() is where it is refused, and the refusal never quotes it.
        var stored = ProviderEndpoint.Parse("{\"baseUrl\":\"https://user:pw-0167@proxy.local/\",\"apiKeyName\":\"k\"}");
        stored.ShouldNotBeNull();
        stored.BaseUrl.ShouldContain("pw-0167", Case.Sensitive, "Parse keeps what it read");

        var userinfo = Should.Throw<ValidationException>(() => ProviderEndpointProjection.For(AgentKind.ClaudeCode, stored));
        userinfo.Code.ShouldBe("provider_endpoint_base_url_userinfo");
        userinfo.Message.ShouldNotContain("pw-0167");
        string.Join(" ", userinfo.Errors.Values.SelectMany(v => v)).ShouldNotContain("pw-0167");

        var quoted = ProviderEndpoint.Parse("{\"baseUrl\":\"https://proxy.local/a\\\"b\",\"apiKeyName\":\"k\"}");
        quoted.ShouldNotBeNull();
        Should.Throw<ValidationException>(() => ProviderEndpointProjection.For(AgentKind.Codex, quoted))
            .Code.ShouldBe("provider_endpoint_base_url_characters");

        var relative = ProviderEndpoint.Parse("{\"baseUrl\":\"proxy.local/v1\",\"apiKeyName\":\"k\"}");
        relative.ShouldNotBeNull();
        Should.Throw<ValidationException>(() => ProviderEndpointProjection.For(AgentKind.Grok, relative))
            .Code.ShouldBe("provider_endpoint_base_url_relative");

        // The project map has the same property: an entry is kept by ParseMap and refused by For.
        var map = ProviderEndpoint.ParseMap("{\"Codex\":{\"baseUrl\":\"https://proxy.local/?t=pw-0167\",\"apiKeyName\":\"k\"}}");
        map.ShouldContainKey(AgentKind.Codex);
        var query = Should.Throw<ValidationException>(() => ProviderEndpointProjection.For(AgentKind.Codex, map[AgentKind.Codex]));
        query.Code.ShouldBe("provider_endpoint_base_url_query");
        query.Message.ShouldNotContain("pw-0167");
    }

    [Test]
    public void C0167_Lookalike_quotes_cannot_close_the_codex_toml_string()
    {
        // U+201C, U+201D, U+FF02 and U+2033 are literal characters, not U+0022: they are accepted,
        // and the emitted base_url argument still carries exactly its own two quotes and no escape.
        foreach (var lookalike in new[] { "\u201C", "\u201D", "\uFF02", "\u2033" })
        {
            var projected = ProviderEndpointProjection.For(
                AgentKind.Codex, new ProviderEndpoint("https://proxy.local/" + lookalike + "x", KeyName));
            var baseUrlArg = projected.Args.Single(a => a.StartsWith("model_providers.antiphon.base_url=", StringComparison.Ordinal));
            baseUrlArg.ShouldBe("model_providers.antiphon.base_url=\"https://proxy.local/" + lookalike + "x/v1\"");
            baseUrlArg.Count(c => c == '"').ShouldBe(2, $"U+{(int)lookalike[0]:X4}");
            baseUrlArg.ShouldNotContain("\\");
        }
    }

    [Test]
    public void C0167_ToString_prints_host_and_key_name_never_the_path()
    {
        // A record's default ToString prints every member, the full URL included; the plan's log rule
        // is host only (a path or query could carry a tenant token). The prose form follows the rule
        // so an interpolated or structured log of the setting cannot leak one.
        var endpoint = new ProviderEndpoint("https://proxy.local:8443/tenant-token-0167", KeyName);
        var prose = endpoint.ToString();
        prose.ShouldContain("proxy.local");
        prose.ShouldContain(KeyName);
        prose.ShouldNotContain("tenant-token-0167");
        prose.ShouldNotContain("8443");
        $"{endpoint}".ShouldNotContain("tenant-token-0167");

        // A stored row that is not a URL prints nothing of itself.
        new ProviderEndpoint("not a url with pw-0167", null).ToString().ShouldNotContain("pw-0167");

        // The column and the DTO still carry the full URL: only prose output is host-only.
        ProviderEndpoint.Serialize(endpoint).ShouldContain("tenant-token-0167");
    }

    private static IEnumerable<string> Names(IReadOnlyDictionary<string, string> env) => env.Keys;

    private static IEnumerable<string> ArgKeys(IReadOnlyList<string> args) =>
        args.Where((_, i) => i % 2 == 1).Select(v => v.Split('=', 2)[0]);

    private static string ArgValue(IReadOnlyList<string> args, string keySuffix) =>
        args.Where((_, i) => i % 2 == 1)
            .Single(v => v.Split('=', 2)[0].EndsWith(keySuffix, StringComparison.Ordinal))
            .Split('=', 2)[1];
}

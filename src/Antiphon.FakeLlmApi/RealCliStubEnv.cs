namespace Antiphon.FakeLlmApi;

/// <summary>
/// The ONLY sanctioned env/args builder for pointing a real CLI at FakeLlmApi.
/// Committed tests must not hand-roll stub env — safety decisions live here.
/// </summary>
public static class RealCliStubEnv
{
    public sealed record LaunchOverlay(
        IReadOnlyDictionary<string, string> Env,
        IReadOnlyList<string> Args);

    /// <summary>
    /// Claude: ANTHROPIC_BASE_URL + ANTHROPIC_API_KEY (x-api-key on the wire), isolated
    /// CLAUDE_CONFIG_DIR, plus ApplyClaudeEnvironmentDefaults values pre-applied.
    /// </summary>
    public static LaunchOverlay ForClaude(string baseUrl, string syntheticKey, string configDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(syntheticKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDir);

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANTHROPIC_BASE_URL"] = TrimTrailingSlash(baseUrl),
            ["ANTHROPIC_API_KEY"] = syntheticKey,
            ["CLAUDE_CONFIG_DIR"] = Path.GetFullPath(configDir),
            ["DISABLE_AUTOUPDATER"] = "1",
            ["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1",
            // Nesting markers emptied so a canary launched under an Antiphon session does not
            // inherit a "already nested" refusal from the parent Claude.
            ["CLAUDECODE"] = "",
            ["CLAUDE_CODE_CHILD_SESSION"] = "",
            ["CLAUDE_CODE_SESSION_ID"] = "",
            ["CLAUDE_CODE_BRIDGE_SESSION_ID"] = "",
            ["CLAUDE_CODE_ENTRYPOINT"] = "",
        };

        return new LaunchOverlay(env, Array.Empty<string>());
    }

    /// <summary>
    /// Grok: BOTH base-URL vars at the stub. Chat redirect is GROK_CLI_CHAT_PROXY_BASE_URL
    /// (canonical). GROK_XAI_API_BASE_URL is defense-in-depth for the /api-key credential oracle
    /// ONLY — it is NOT a safe chat redirect (CARD-0168 decision 4). Never touches GROK_AUTH_PATH.
    /// </summary>
    public static LaunchOverlay ForGrok(string baseUrl, string syntheticKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(syntheticKey);

        var trimmed = TrimTrailingSlash(baseUrl);

        // Executable form of the GROK_XAI_API_BASE_URL ban: the chat-proxy var is mandatory.
        // A caller cannot construct a "safe-looking" overlay that omits it.
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GROK_CLI_CHAT_PROXY_BASE_URL"] = trimmed,
            ["GROK_XAI_API_BASE_URL"] = trimmed,
            ["GROK_CODE_XAI_API_KEY"] = syntheticKey,
            ["GROK_TELEMETRY_ENABLED"] = "0",
            ["GROK_FEEDBACK_ENABLED"] = "0",
        };

        if (!env.ContainsKey("GROK_CLI_CHAT_PROXY_BASE_URL")
            || string.IsNullOrWhiteSpace(env["GROK_CLI_CHAT_PROXY_BASE_URL"]))
        {
            throw new InvalidOperationException(
                "GROK_CLI_CHAT_PROXY_BASE_URL is required. GROK_XAI_API_BASE_URL alone is a false " +
                "safety (redirects only /api-key; chat still hits real xAI). See CARD-0168.");
        }

        return new LaunchOverlay(env, Array.Empty<string>());
    }

    /// <summary>
    /// Codex: OPENAI_API_KEY plus the FIVE <c>-c</c> launch arguments
    /// (model_providers.stub.{{name,base_url,env_key,wire_api}} + model_provider=stub).
    /// Base URL for the provider includes <c>/v1</c> — Codex hits <c>/v1/models</c> and
    /// <c>/v1/responses</c> relative to that.
    /// </summary>
    public static LaunchOverlay ForCodex(string baseUrl, string syntheticKey, string codexHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(syntheticKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);

        var providerBase = TrimTrailingSlash(baseUrl);
        if (!providerBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            providerBase += "/v1";

        // Codex only refreshes a custom provider's /models endpoint when its local auth state
        // identifies a ChatGPT account. A disposable, unsigned fixture token enables that code
        // path; the provider's Authorization header still comes from OPENAI_API_KEY below.
        SeedCodexHome(codexHome);

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPENAI_API_KEY"] = syntheticKey,
            // A real Codex CLI persists every exec run under CODEX_HOME. Stub-proxy tests must
            // never inherit the operator's home, which would put synthetic test threads in the
            // Codex Desktop sidebar.
            ["CODEX_HOME"] = Path.GetFullPath(codexHome),
        };

        // Five -c overrides. Quoted values match the probe-confirmed launch line.
        var args = new List<string>
        {
            "-c", "model_providers.stub.name=\"Stub\"",
            "-c", $"model_providers.stub.base_url=\"{providerBase}\"",
            "-c", "model_providers.stub.env_key=\"OPENAI_API_KEY\"",
            "-c", "model_providers.stub.wire_api=\"responses\"",
            "-c", "model_provider=stub",
        };

        return new LaunchOverlay(env, args);
    }

    private static void SeedCodexHome(string codexHome)
    {
        Directory.CreateDirectory(codexHome);
        // This is intentionally synthetic and unsigned: it is only local bootstrap metadata for
        // a throwaway CODEX_HOME, never a credential or a token capable of authenticating.
        File.WriteAllText(Path.Combine(codexHome, "auth.json"), """
            {
              "auth_mode": "chatgpt",
              "tokens": {
                "id_token": "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJleHAiOjQxMDI0NDQ4MDAsImh0dHBzOi8vYXBpLm9wZW5haS5jb20vYXV0aCI6eyJjaGF0Z3B0X3BsYW5fdHlwZSI6InBybyIsImNoYXRncHRfdXNlcl9pZCI6InN0dWItdXNlciIsImNoYXRncHRfYWNjb3VudF9pZCI6InN0dWItYWNjb3VudCJ9fQ.stub-signature",
                "access_token": "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJleHAiOjQxMDI0NDQ4MDB9.stub-signature",
                "refresh_token": "stub-refresh-token",
                "account_id": "stub-account"
              },
              "last_refresh": "2026-01-01T00:00:00Z"
            }
            """);

        // CARD-0133 S0 (measured 2026-08-30, codex-cli 0.151.0): an isolated CODEX_HOME carries no
        // Windows sandbox decision, so the interactive TUI shows the "Set up the Codex agent sandbox"
        // prompt right after the trust decision (codex-rs/tui/src/lib.rs:
        // trust_decision_was_made && level == Disabled). Its default choice runs the ELEVATED setup
        // in-process — "Setting up sandbox... Input disabled until setup completes." — and the
        // composer never becomes usable. "unelevated" is a decision that needs no setup; the harness
        // launches with --dangerously-bypass-approvals-and-sandbox anyway. The operator's real
        // ~/.codex has `[windows] sandbox = "elevated"` with setup already completed, which is why
        // production sessions never see this.
        //
        // features.apps=false keeps the built-in `codex_apps` MCP client off: with the synthetic
        // ChatGPT auth above it would otherwise call the REAL ChatGPT backend and fail 401
        // ("Could not parse your authentication token"), printing "MCP startup incomplete" on the
        // boot screen. The /v1/models refresh the auth exists for is gated on auth mode, not apps.
        //
        // Codex edits this file in place when the trust prompt is accepted (adds [projects.*]);
        // the keys below survive that.
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), """
            [windows]
            sandbox = "unelevated"

            [features]
            apps = false
            """);
    }

    private static string TrimTrailingSlash(string url)
        => url.TrimEnd('/');
}

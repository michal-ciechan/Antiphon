using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Projects a <see cref="ProviderEndpoint"/> into the launch environment and, for Codex, the launch
/// arguments, one arm per agent kind (CARD-0167 S1, plan D-3). The production counterpart of the
/// test-only <c>Antiphon.FakeLlmApi.RealCliStubEnv</c>: same variable names, same five Codex
/// <c>-c</c> keys, a different provider id (<see cref="CodexProviderId"/>), so a stub canary and a
/// production redirect never share a provider table entry.
///
/// <para><b>This class never takes a key value and cannot emit one.</b> The key appears only as the
/// <c>{{key:NAME}}</c> placeholder in an environment VALUE; <c>ApiKeyEnvResolver</c> resolves it last,
/// and <see cref="ApiKeyPlaceholder.EnsureAbsent"/> still runs over every argument after projection,
/// which is what keeps the key out of argv by construction. Per-kind asymmetry (plan ground truth):
/// Claude reads <c>ANTHROPIC_BASE_URL</c>; Grok needs BOTH <c>GROK_CLI_CHAT_PROXY_BASE_URL</c> (chat
/// turns) and <c>GROK_XAI_API_BASE_URL</c> (<c>/api-key</c> only; alone it is a false safety, CARD-0168
/// decision 4); Codex has no base-URL variable at all and takes the provider table through
/// <c>-c</c>, with its key always named <see cref="CodexKeyVariable"/> so
/// <c>PhoneHomeLaunchPolicy</c>'s runner refusal stays effective (plan D-5).</para>
///
/// <para>It never sets, reads or writes <c>CLAUDE_CONFIG_DIR</c>, <c>GROK_AUTH_PATH</c>,
/// <c>CODEX_HOME</c> or any auth file: those belong to the CLIs' own sign-in state.</para>
/// </summary>
public static class ProviderEndpointProjection
{
    /// <summary>The Codex <c>model_providers.&lt;id&gt;</c> table entry this projection owns.</summary>
    public const string CodexProviderId = "antiphon";

    /// <summary>The human-readable provider name Codex shows for that entry.</summary>
    public const string CodexProviderName = "Antiphon endpoint";

    /// <summary>The one variable the Codex arm names in <c>env_key</c> and sets (plan D-5).</summary>
    public const string CodexKeyVariable = "OPENAI_API_KEY";

    /// <summary>The literal placeholders <see cref="Describe"/> uses in place of a URL and a key name.</summary>
    public const string UrlPlaceholder = "<url>";
    public const string KeyNamePlaceholder = "<name>";

    /// <summary>What a projection adds: environment entries (Ordinal names) and argv elements, appended after the caller's own arguments.</summary>
    public sealed record Projected(IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> Args)
    {
        public static readonly Projected Empty = new(
            new Dictionary<string, string>(StringComparer.Ordinal), Array.Empty<string>());
    }

    /// <summary>
    /// The launch overlay for one kind and one validated-or-stored endpoint. Refuses 409
    /// <c>provider_endpoint_unsupported_kind</c> for a kind the setting does not apply to and 409
    /// <c>provider_endpoint_key_required</c> for Codex without a key name (the save-time 422s of
    /// <see cref="ProviderEndpoint.Validate"/> are the same rules on the other side of the store).
    /// The endpoint is validated again here, so a row written around the API still cannot project a
    /// placeholder, a quote or a relative URL into a real launch.
    /// </summary>
    public static Projected For(AgentKind kind, ProviderEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!ProviderEndpoint.IsSupportedKind(kind))
        {
            throw new ConflictException(
                $"A provider endpoint cannot be projected for agent kind '{kind}'; only "
                + string.Join(", ", ProviderEndpoint.SupportedKinds) + " are supported.",
                "provider_endpoint_unsupported_kind");
        }

        if (ProviderEndpoint.RequiresKeyName(kind) && string.IsNullOrWhiteSpace(endpoint.ApiKeyName))
        {
            throw new ConflictException(
                $"A {kind} provider endpoint needs an API key name: the CLI's env_key must name a set "
                + $"variable, so the projection sets {CodexKeyVariable} from that key.",
                "provider_endpoint_key_required");
        }

        var valid = endpoint.Validate(kind);
        var placeholder = valid.ApiKeyName is null
            ? null
            : ApiKeyPlaceholder.Marker + valid.ApiKeyName + "}}";
        return Build(kind, valid.BaseUrl, placeholder);
    }

    /// <summary>
    /// The same shape with <see cref="UrlPlaceholder"/> and <c>{{key:&lt;name&gt;}}</c> as literal
    /// placeholders, for the UI and docs (names only). Empty, never a throw, for a kind the setting
    /// does not apply to, so a catalogue can call it for every kind.
    /// </summary>
    public static Projected Describe(AgentKind kind) =>
        ProviderEndpoint.IsSupportedKind(kind)
            ? Build(kind, UrlPlaceholder, ApiKeyPlaceholder.Marker + KeyNamePlaceholder + "}}")
            : Projected.Empty;

    private static Projected Build(AgentKind kind, string url, string? keyPlaceholder)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (kind)
        {
            case AgentKind.ClaudeCode:
                env["ANTHROPIC_BASE_URL"] = url;
                if (keyPlaceholder is not null)
                    env["ANTHROPIC_API_KEY"] = keyPlaceholder;
                return new Projected(env, Array.Empty<string>());

            case AgentKind.Grok:
                // Both, chat var mandatory (plan D-6). GROK_BASE_URL is the gkp wrapper's own
                // convention and is not projected.
                env["GROK_CLI_CHAT_PROXY_BASE_URL"] = url;
                env["GROK_XAI_API_BASE_URL"] = url;
                if (keyPlaceholder is not null)
                    env["GROK_CODE_XAI_API_KEY"] = keyPlaceholder;
                return new Projected(env, Array.Empty<string>());

            case AgentKind.Codex:
                // Guaranteed non-null by For (key required) and Describe (literal placeholder).
                env[CodexKeyVariable] = keyPlaceholder!;
                var providerBase = url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : url + "/v1";
                // Five -c overrides in the probe-confirmed form (RealCliStubEnv.ForCodex), quoted
                // values included; the URL was validated free of quotes, backslashes and whitespace.
                string[] args =
                [
                    CodexLaunchArgs.ConfigFlag, $"model_providers.{CodexProviderId}.name=\"{CodexProviderName}\"",
                    CodexLaunchArgs.ConfigFlag, $"model_providers.{CodexProviderId}.base_url=\"{providerBase}\"",
                    CodexLaunchArgs.ConfigFlag, $"model_providers.{CodexProviderId}.env_key=\"{CodexKeyVariable}\"",
                    CodexLaunchArgs.ConfigFlag, $"model_providers.{CodexProviderId}.wire_api=\"responses\"",
                    CodexLaunchArgs.ConfigFlag, $"model_provider={CodexProviderId}",
                ];
                return new Projected(env, args);

            default:
                throw new ConflictException(
                    $"A provider endpoint cannot be projected for agent kind '{kind}'.",
                    "provider_endpoint_unsupported_kind");
        }
    }
}

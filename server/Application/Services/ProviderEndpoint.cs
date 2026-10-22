using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The provider endpoint setting (CARD-0167 S1): the trusted proxy an agent kind's CLI talks to, and
/// the NAME of the <c>ApiKey</c> store entry that authenticates it.
///
/// <para><b>Custody.</b> This record holds a URL and a key name, never a key value, header value or
/// argument text. The value stays where CARD-0106 keeps it (the Data-Protection-encrypted
/// <c>ApiKey</c> store, write-only over HTTP) and reaches a child process only through the
/// <c>{{key:NAME}}</c> placeholder that <see cref="ProviderEndpointProjection"/> emits into an
/// environment VALUE, resolved last by <c>ApiKeyEnvResolver</c>. Rotation is a key-store write; the
/// setting does not change (plan D-1).</para>
///
/// <para><b>Parsing never throws</b>, like <see cref="AgentLaunchEnv.Parse"/>: a stored row with
/// unreadable JSON reads as "no endpoint" rather than taking an agent offline over a column it may
/// not use. Writes are validated through <see cref="Validate"/>, where a malformed value can be
/// refused to somebody's face.</para>
/// </summary>
public sealed record ProviderEndpoint(string BaseUrl, string? ApiKeyName)
{
    public const int MaxBaseUrlLength = 2000;

    /// <summary>The field prefix the 422s name when the caller gives none.</summary>
    public const string DefaultField = "providerEndpoint";

    private static readonly AgentKind[] Supported = [AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex];

    private static readonly IReadOnlyDictionary<AgentKind, ProviderEndpoint> EmptyMap =
        new Dictionary<AgentKind, ProviderEndpoint>();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>The kinds the setting applies to: the three real CLIs with a known redirect contract.</summary>
    public static IReadOnlyList<AgentKind> SupportedKinds => Supported;

    /// <summary>The empty project map: no endpoint for any kind (CARD-0167 S3 callers' "none").</summary>
    public static IReadOnlyDictionary<AgentKind, ProviderEndpoint> NoEndpoints => EmptyMap;

    public static bool IsSupportedKind(AgentKind kind) => Array.IndexOf(Supported, kind) >= 0;

    /// <summary>
    /// Codex's <c>env_key</c> must name a SET variable, so a Codex endpoint without a key name would
    /// launch a CLI that authenticates as nobody (plan D-5). Claude and Grok accept a URL-only
    /// endpoint: a key-injecting proxy such as gkp needs no name.
    /// </summary>
    public static bool RequiresKeyName(AgentKind kind) => kind == AgentKind.Codex;

    /// <summary>
    /// Validates the setting and returns it normalised (trimmed, trailing slash removed, blank key
    /// name as <c>null</c>). Throws 422 naming <c>&lt;field&gt;.baseUrl</c> or
    /// <c>&lt;field&gt;.apiKeyName</c> and a <c>provider_endpoint_*</c> code; the URL itself is never
    /// quoted in the message, because a path or query could carry a tenant token.
    /// </summary>
    /// <param name="kind">
    /// The kind the endpoint is for, when known. The project map knows it per entry; the agent path
    /// may not know it until resolve time, in which case the kind rules are applied there
    /// (<see cref="ProviderEndpointProjection.For"/>).
    /// </param>
    /// <param name="field">The request field the errors are reported under.</param>
    public ProviderEndpoint Validate(AgentKind? kind, string field = DefaultField)
    {
        var urlField = field + ".baseUrl";
        var nameField = field + ".apiKeyName";

        if (kind is { } unsupported && !IsSupportedKind(unsupported))
        {
            throw new ValidationException(
                field,
                $"A provider endpoint is not supported for agent kind '{unsupported}'. Supported kinds: "
                + string.Join(", ", Supported) + ".",
                "provider_endpoint_unsupported_kind");
        }

        var baseUrl = BaseUrl?.Trim() ?? string.Empty;
        if (baseUrl.Length == 0)
        {
            throw new ValidationException(
                urlField, "A base URL is required.", "provider_endpoint_base_url_required");
        }
        if (ApiKeyPlaceholder.ContainsMarker(baseUrl))
        {
            throw new ValidationException(
                urlField,
                "The base URL may not carry an API key placeholder. The key is referenced by NAME in "
                + "apiKeyName and the projection writes the placeholder itself.",
                "provider_endpoint_placeholder");
        }
        if (baseUrl.Length > MaxBaseUrlLength)
        {
            throw new ValidationException(
                urlField,
                $"A base URL may be at most {MaxBaseUrlLength} characters.",
                "provider_endpoint_base_url_too_long");
        }
        if (baseUrl.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '"' || c == '\\'))
        {
            // The Codex arm quotes the URL into a -c TOML string; a quote, backslash or whitespace
            // would change what the CLI parses, so none of them is a URL this setting accepts.
            throw new ValidationException(
                urlField,
                "A base URL may not contain whitespace, control characters, quotes or backslashes.",
                "provider_endpoint_base_url_characters");
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ValidationException(
                urlField,
                "The base URL must be absolute, with a scheme and a host, for example "
                + "https://proxy.example.com:8443 or http://127.0.0.1:8080.",
                "provider_endpoint_base_url_relative");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidationException(
                urlField, "The base URL scheme must be http or https.", "provider_endpoint_base_url_scheme");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ValidationException(
                urlField,
                "The base URL may not carry user information. Credentials belong in the API key store, "
                + "referenced by apiKeyName.",
                "provider_endpoint_base_url_userinfo");
        }
        if (!string.IsNullOrEmpty(uri.Query))
        {
            throw new ValidationException(
                urlField, "The base URL may not carry a query string.", "provider_endpoint_base_url_query");
        }
        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ValidationException(
                urlField, "The base URL may not carry a fragment.", "provider_endpoint_base_url_fragment");
        }

        baseUrl = baseUrl.TrimEnd('/');

        string? name = string.IsNullOrWhiteSpace(ApiKeyName) ? null : ApiKeyName.Trim();
        if (name is not null)
        {
            if (ApiKeyPlaceholder.ContainsMarker(name))
            {
                throw new ValidationException(
                    nameField,
                    "apiKeyName is the NAME of a stored API key, not a placeholder: write the name "
                    + "without {{key:...}}.",
                    "provider_endpoint_placeholder");
            }

            name = ApiKeyNaming.Validate(name, nameField);
        }
        else if (kind is { } requiresKey && RequiresKeyName(requiresKey))
        {
            throw new ValidationException(
                nameField,
                $"A {requiresKey} endpoint needs an API key name: the CLI's env_key must name a set "
                + $"variable, so the projection sets {ProviderEndpointProjection.CodexKeyVariable} from "
                + "that key.",
                "provider_endpoint_key_required");
        }

        return new ProviderEndpoint(baseUrl, name);
    }

    /// <summary>One stored endpoint (the agent column). Blank, malformed or URL-less JSON reads as <c>null</c>.</summary>
    public static ProviderEndpoint? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<ProviderEndpoint>(json, JsonOptions);
            return parsed is null || string.IsNullOrWhiteSpace(parsed.BaseUrl) ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The agent column's JSON, or <c>null</c> for no endpoint.</summary>
    public static string? Serialize(ProviderEndpoint? endpoint) =>
        endpoint is null ? null : JsonSerializer.Serialize(endpoint, JsonOptions);

    /// <summary>
    /// The project column: a dictionary keyed by <see cref="AgentKind"/> NAME (Ordinal, never the
    /// numeric value). Entries with an unknown kind, a null body or a blank URL are skipped; malformed
    /// JSON reads as empty.
    /// </summary>
    public static IReadOnlyDictionary<AgentKind, ProviderEndpoint> ParseMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return EmptyMap;

        Dictionary<string, ProviderEndpoint?>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, ProviderEndpoint?>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return EmptyMap;
        }

        if (raw is null || raw.Count == 0)
            return EmptyMap;

        var result = new Dictionary<AgentKind, ProviderEndpoint>();
        foreach (var (key, value) in raw)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.BaseUrl))
                continue;
            if (!TryParseKind(key, out var kind))
                continue;
            result[kind] = value;
        }

        return result.Count == 0 ? EmptyMap : result;
    }

    /// <summary>The project column's JSON; <c>{}</c> for no endpoints. Keys are kind names, sorted for a stable row.</summary>
    public static string SerializeMap(IReadOnlyDictionary<AgentKind, ProviderEndpoint>? map)
    {
        if (map is null || map.Count == 0)
            return "{}";

        var ordered = new SortedDictionary<string, ProviderEndpoint>(StringComparer.Ordinal);
        foreach (var (kind, endpoint) in map)
            ordered[kind.ToString()] = endpoint;
        return JsonSerializer.Serialize(ordered, JsonOptions);
    }

    /// <summary>A kind by its enum NAME only: <c>"1"</c> is not a kind an operator wrote.</summary>
    public static bool TryParseKind(string? name, out AgentKind kind)
    {
        foreach (var candidate in Enum.GetValues<AgentKind>())
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}

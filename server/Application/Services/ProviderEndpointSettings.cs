using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Validates, stores and reads the per-kind provider endpoint setting (CARD-0167 S2): a base URL
/// plus the NAME of a key in the existing <see cref="ApiKey"/> store. Nothing here touches a key
/// value, and no refusal message echoes the URL it refused (userinfo in a URL is a credential).
///
/// <para>Parsing never throws, like <see cref="AgentLaunchEnv"/>: an unreadable column reads as
/// "no endpoint" and the write side is where a malformed value is refused to somebody's face.</para>
///
/// <para>Seam with S1: the pure value object and projection (<c>ProviderEndpoint</c>,
/// <c>ProviderEndpointProjection</c>) are S1's; S3 wires the stored shape here into the launch
/// resolvers. This class owns only what persistence and the API need.</para>
/// </summary>
public static class ProviderEndpointSettings
{
    public const int MaxBaseUrlLength = 2000;

    public const string UnsupportedKindCode = "provider_endpoint_unsupported_kind";
    public const string KeyRequiredCode = "provider_endpoint_key_required";
    public const string InvalidUrlCode = "provider_endpoint_invalid_url";
    public const string EndpointRequiredCode = "provider_endpoint_required";
    public const string NotApplicableCode = "provider_endpoint_not_applicable";
    public const string KeyNotFoundWarning = "provider_endpoint_key_not_found";

    public static readonly IReadOnlyList<AgentKind> SupportedKinds =
        [AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool IsSupported(AgentKind kind) =>
        kind is AgentKind.ClaudeCode or AgentKind.Grok or AgentKind.Codex;

    /// <summary>Codex names the key variable through <c>env_key</c>, so it must name a key.</summary>
    public static bool RequiresKeyName(AgentKind kind) => kind == AgentKind.Codex;

    /// <summary>
    /// Validates one endpoint for <paramref name="kind"/> and returns it normalised (trimmed,
    /// trailing slash removed). Throws 422 naming <paramref name="field"/> or a sub-field.
    /// </summary>
    public static ProviderEndpointDto Validate(ProviderEndpointDto? endpoint, AgentKind kind, string field)
    {
        if (endpoint is null)
            throw new ValidationException(field, "A provider endpoint (baseUrl, optional apiKeyName) is required.", EndpointRequiredCode);
        if (!IsSupported(kind))
        {
            throw new ValidationException(
                field,
                $"Provider endpoints are supported for ClaudeCode, Grok and Codex agents, not {kind}.",
                UnsupportedKindCode);
        }

        var baseUrl = ValidateBaseUrl(endpoint.BaseUrl, $"{field}.baseUrl");
        var keyName = string.IsNullOrWhiteSpace(endpoint.ApiKeyName)
            ? null
            : ApiKeyNaming.Validate(endpoint.ApiKeyName, $"{field}.apiKeyName");
        if (keyName is null && RequiresKeyName(kind))
        {
            throw new ValidationException(
                $"{field}.apiKeyName",
                $"A {kind} endpoint needs an API key name: its launch names the variable that must carry the key.",
                KeyRequiredCode);
        }

        return new ProviderEndpointDto(baseUrl, keyName);
    }

    /// <summary>
    /// Absolute http/https, no userinfo, no query, no fragment, no placeholder, at most
    /// <see cref="MaxBaseUrlLength"/> characters; the trailing slash is trimmed. A loopback host is
    /// fine (local key proxies are the norm). The refused value is never part of the message.
    /// <para>Review e4b591eb D2: after these rules (whose codes and messages the S2 API tests pin),
    /// the launch path's own rules run (<see cref="ProviderEndpoint.ValidateBaseUrl"/>: Unicode
    /// whitespace and C1 controls, quotes and backslashes, the port range), under their own
    /// <c>provider_endpoint_base_url_*</c> codes. The save path therefore refuses everything the
    /// launch path refuses, so a stored URL cannot be refused at every launch after a 200.</para>
    /// </summary>
    public static string ValidateBaseUrl(string? value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ValidationException(field, "A base URL is required.", InvalidUrlCode);
        if (trimmed.Length > MaxBaseUrlLength)
            throw new ValidationException(field, $"A base URL may be at most {MaxBaseUrlLength} characters.", InvalidUrlCode);
        if (ApiKeyPlaceholder.ContainsMarker(trimmed))
            throw new ValidationException(field, "A base URL cannot carry an API key placeholder.", InvalidUrlCode);
        if (trimmed.Any(c => c <= ' ' || c == '\x7f'))
            throw new ValidationException(field, "A base URL cannot contain whitespace or control characters.", InvalidUrlCode);
        if (trimmed.Contains('?', StringComparison.Ordinal) || trimmed.Contains('#', StringComparison.Ordinal))
            throw new ValidationException(field, "A base URL cannot carry a query string or a fragment.", InvalidUrlCode);
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Host.Length == 0
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationException(field, "A base URL must be an absolute http or https URL.", InvalidUrlCode);
        }
        if (uri.UserInfo.Length > 0)
            throw new ValidationException(field, "A base URL cannot carry userinfo; reference a stored key by name instead.", InvalidUrlCode);

        var normalised = trimmed.TrimEnd('/');
        if (!Uri.TryCreate(normalised, UriKind.Absolute, out _))
            throw new ValidationException(field, "A base URL must be an absolute http or https URL.", InvalidUrlCode);
        return ProviderEndpoint.ValidateBaseUrl(normalised, field);
    }

    /// <summary>
    /// Validates a project map keyed by agent kind name. Keys are matched case-insensitively and
    /// stored under the canonical enum name; an unsupported or unknown kind is 422
    /// <see cref="UnsupportedKindCode"/> naming <c>field.Kind</c>. An empty map is the explicit clear.
    /// </summary>
    public static IReadOnlyDictionary<string, ProviderEndpointDto> ValidateMap(
        IReadOnlyDictionary<string, ProviderEndpointDto?>? map,
        string field)
    {
        if (map is null || map.Count == 0)
            return ProviderEndpointDto.EmptyMap;

        var result = new SortedDictionary<string, ProviderEndpointDto>(StringComparer.Ordinal);
        foreach (var (rawKind, endpoint) in map)
        {
            var kindField = $"{field}.{rawKind}";
            var kindName = rawKind?.Trim() ?? string.Empty;
            if (kindName.Length == 0
                || int.TryParse(kindName, out _)
                || !Enum.TryParse<AgentKind>(kindName, ignoreCase: true, out var kind)
                || !Enum.IsDefined(kind)
                || !IsSupported(kind))
            {
                throw new ValidationException(
                    kindField,
                    "Provider endpoints are keyed by a supported agent kind: ClaudeCode, Grok or Codex.",
                    UnsupportedKindCode);
            }

            var canonical = kind.ToString();
            if (result.ContainsKey(canonical))
                throw new ValidationException(kindField, $"The {canonical} endpoint is listed twice.", UnsupportedKindCode);
            result[canonical] = Validate(endpoint, kind, $"{field}.{canonical}");
        }

        return result;
    }

    public static string Serialize(ProviderEndpointDto endpoint) =>
        JsonSerializer.Serialize(new Stored(endpoint.BaseUrl, endpoint.ApiKeyName), JsonOptions);

    public static string SerializeMap(IReadOnlyDictionary<string, ProviderEndpointDto>? map)
    {
        if (map is null || map.Count == 0)
            return "{}";
        var ordered = new SortedDictionary<string, Stored>(StringComparer.Ordinal);
        foreach (var (kind, endpoint) in map)
            ordered[kind] = new Stored(endpoint.BaseUrl, endpoint.ApiKeyName);
        return JsonSerializer.Serialize(ordered, JsonOptions);
    }

    /// <summary>Never throws; null for an empty, unreadable or URL-less column.</summary>
    public static ProviderEndpointDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return ToDto(JsonSerializer.Deserialize<Stored>(json, JsonOptions));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Never throws; entries with an unknown kind or no URL are dropped.</summary>
    public static IReadOnlyDictionary<string, ProviderEndpointDto> ParseMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ProviderEndpointDto.EmptyMap;

        Dictionary<string, Stored?>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, Stored?>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return ProviderEndpointDto.EmptyMap;
        }
        if (parsed is null || parsed.Count == 0)
            return ProviderEndpointDto.EmptyMap;

        var result = new SortedDictionary<string, ProviderEndpointDto>(StringComparer.Ordinal);
        foreach (var (kind, stored) in parsed)
        {
            if (!Enum.TryParse<AgentKind>(kind, ignoreCase: false, out var parsedKind) || !Enum.IsDefined(parsedKind))
                continue;
            if (ToDto(stored) is { } endpoint)
                result[parsedKind.ToString()] = endpoint;
        }

        return result.Count == 0 ? ProviderEndpointDto.EmptyMap : result;
    }

    /// <summary>
    /// D-8: a key name that resolves nowhere is a warning on the save response, not a refusal (the
    /// key may be written next; the launch is the loud gate). Names only, sorted by kind.
    /// </summary>
    public static async Task<IReadOnlyList<string>> MissingKeyWarningsAsync(
        IQueryable<ApiKey> keys,
        Guid? projectId,
        IReadOnlyDictionary<string, ProviderEndpointDto> endpoints,
        CancellationToken ct)
    {
        var names = endpoints.Values
            .Select(e => e.ApiKeyName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
            return [];

        var present = await keys
            .Where(k => names.Contains(k.Name) && (k.ProjectId == null || k.ProjectId == projectId))
            .Select(k => k.Name)
            .ToListAsync(ct);
        var presentSet = new HashSet<string>(present, StringComparer.Ordinal);

        var warnings = new List<string>();
        foreach (var (kind, endpoint) in endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (endpoint.ApiKeyName is { } name && !presentSet.Contains(name))
            {
                warnings.Add(
                    $"{KeyNotFoundWarning}: no API key named '{name}' exists at project or global scope for the "
                    + $"{kind} endpoint; write it before the next launch or that launch is refused by name.");
            }
        }

        return warnings;
    }

    private static ProviderEndpointDto? ToDto(Stored? stored) =>
        stored is null || string.IsNullOrWhiteSpace(stored.BaseUrl)
            ? null
            : new ProviderEndpointDto(
                stored.BaseUrl,
                string.IsNullOrWhiteSpace(stored.ApiKeyName) ? null : stored.ApiKeyName);

    /// <summary>The column shape: exactly the two fields, so a value can never be stored by accident.</summary>
    private sealed record Stored(string? BaseUrl, string? ApiKeyName);
}

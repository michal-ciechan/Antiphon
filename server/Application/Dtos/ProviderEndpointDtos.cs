namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// One provider endpoint as the operator wrote it (CARD-0167 S2): the trusted proxy's base URL
/// and the NAME of a stored API key, never a key value. The same shape is stored on the project
/// (one per agent kind) and on an agent in <c>Endpoint</c> mode, and echoed back unchanged.
/// </summary>
public sealed record ProviderEndpointDto(string BaseUrl, string? ApiKeyName = null)
{
    public static readonly IReadOnlyDictionary<string, ProviderEndpointDto> EmptyMap =
        new Dictionary<string, ProviderEndpointDto>(StringComparer.Ordinal);
}

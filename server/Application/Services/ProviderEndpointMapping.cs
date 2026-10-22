using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0167 S3: the one mapping between the API shape (<see cref="ProviderEndpointDto"/>, S2) and
/// the value object the resolvers project (<see cref="ProviderEndpoint"/>, S1). Both carry a base
/// URL and a key NAME only; there is no value field on either side, so nothing here can move one.
/// </summary>
public static class ProviderEndpointMapping
{
    public static ProviderEndpoint ToRecord(ProviderEndpointDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new ProviderEndpoint(dto.BaseUrl, dto.ApiKeyName);
    }

    public static ProviderEndpointDto ToDto(ProviderEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new ProviderEndpointDto(endpoint.BaseUrl, endpoint.ApiKeyName);
    }

    /// <summary>A project map keyed by kind NAME (the DTO shape) to one keyed by kind; unknown names are skipped, like <see cref="ProviderEndpoint.ParseMap"/>.</summary>
    public static IReadOnlyDictionary<AgentKind, ProviderEndpoint> ToRecords(
        IReadOnlyDictionary<string, ProviderEndpointDto>? map)
    {
        if (map is null || map.Count == 0)
            return ProviderEndpoint.NoEndpoints;

        var result = new Dictionary<AgentKind, ProviderEndpoint>();
        foreach (var (name, dto) in map)
        {
            if (dto is not null && ProviderEndpoint.TryParseKind(name, out var kind))
                result[kind] = ToRecord(dto);
        }

        return result.Count == 0 ? ProviderEndpoint.NoEndpoints : result;
    }

    public static IReadOnlyDictionary<string, ProviderEndpointDto> ToDtos(
        IReadOnlyDictionary<AgentKind, ProviderEndpoint>? map)
    {
        if (map is null || map.Count == 0)
            return ProviderEndpointDto.EmptyMap;

        var result = new Dictionary<string, ProviderEndpointDto>(StringComparer.Ordinal);
        foreach (var (kind, endpoint) in map)
            result[kind.ToString()] = ToDto(endpoint);
        return result;
    }
}

namespace Antiphon.Server.Application.Dtos;

/// <summary>Advertised API capability names on <c>GET /api/version</c> (CARD-0495).</summary>
public static class AntiphonCapabilities
{
    /// <summary>
    /// POST <c>/api/agent-tasks/{id}/land/v2</c> implements the CARD-0488 approval contract.
    /// </summary>
    public const string LandV2 = "land-v2";
}

/// <summary>Build identity of this server process (CARD-0179 R3). <c>GET /api/version</c>.</summary>
public sealed record AntiphonVersionDto(
    string Version,
    string InformationalVersion,
    IReadOnlyList<string> Capabilities);

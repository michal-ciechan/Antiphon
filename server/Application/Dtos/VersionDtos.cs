namespace Antiphon.Server.Application.Dtos;

/// <summary>Build identity of this server process (CARD-0179 R3). <c>GET /api/version</c>.</summary>
public sealed record AntiphonVersionDto(
    string Version,
    string InformationalVersion);

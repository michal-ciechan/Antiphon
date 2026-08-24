using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class VersionEndpoints
{
    /// <summary>
    /// CARD-0179 R3. A tiny JSON identity endpoint rather than a custom /health writer:
    /// <c>SmokeTests.Health_endpoint_returns_healthy</c> pins /health's body as the literal
    /// <c>Healthy</c>, so splicing a SHA in there would break liveness checks.
    /// </summary>
    public static void MapVersionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/version", () => Results.Ok(new AntiphonVersionDto(
            AntiphonVersion.Sha,
            AntiphonVersion.Informational)))
            .WithTags("Version");
    }
}

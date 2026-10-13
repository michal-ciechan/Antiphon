using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class RolePolicyEndpoints
{
    /// <summary>
    /// CARD-0097: each routable role's RolePolicy entry beside its active stage-wide pin
    /// (RolePolicyEndpointTests).
    /// </summary>
    public static void MapRolePolicyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/role-policy", async (RolePolicyReadService policy, CancellationToken ct) =>
                Results.Ok(await policy.ReadAsync(ct)))
            .WithTags("RolePolicy");
    }
}

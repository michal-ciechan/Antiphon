using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// Operator issue / rotate / revoke / list for Delegation Capability principals (CARD-0398).
/// Issue and rotate return the raw token once; GET never includes it. Localhost trust is the
/// existing API model — this scopes the agent, it does not replace loopback trust.
/// </summary>
public static class DelegationCapabilityEndpoints
{
    public static void MapDelegationCapabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/delegation-capabilities").WithTags("Delegation Capabilities");

        group.MapPost("/", async (
            IssueDelegationCapabilityRequest request,
            DelegationCapabilityService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.IssueAsync(request, cancellationToken);
            return Results.Created($"/api/delegation-capabilities/{created.Id}", created);
        });

        group.MapGet("/", async (
            DelegationCapabilityService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            DelegationCapabilityService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(id, cancellationToken)));

        group.MapPost("/{id:guid}/rotate", async (
            Guid id,
            DelegationCapabilityService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RotateAsync(id, cancellationToken)));

        group.MapPost("/{id:guid}/revoke", async (
            Guid id,
            DelegationCapabilityService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RevokeAsync(id, cancellationToken)));
    }
}

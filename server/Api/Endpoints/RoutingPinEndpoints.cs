using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class RoutingPinEndpoints
{
    /// <summary>
    /// Per-card/stage routing pins (CARD-0305). Deliberately its own group and NOT hung off
    /// <c>/api/agent-tasks/pipeline</c>: that is a frozen read aggregation, and a pin is a write.
    /// </summary>
    public static void MapRoutingPinEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/routing-pins").WithTags("RoutingPins");

        group.MapGet("/", async (
            string? card,
            string? role,
            RoutingPinService pins,
            CancellationToken ct) =>
            Results.Ok(new RoutingPinListDto(await pins.ListAsync(card, ParseRole(role), ct))));

        group.MapPut("/", async (
            PutRoutingPinRequest request,
            HttpContext http,
            RoutingPinService pins,
            AgentTaskService tasks,
            CancellationToken ct) =>
        {
            // Provenance is ASSERTED by the caller, never inferred from the bearer — an
            // orchestrator records Human when the operator said so. The token only supplies the
            // audit trail of which delegate wrote the row.
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            return Results.Ok(await pins.UpsertAsync(request, caller?.Task?.Id, ct));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            RoutingPinService pins,
            CancellationToken ct) =>
        {
            // 204 whether it was active or already cleared: clearing twice is how a script that
            // ran twice behaves, and that is not an error.
            var found = await pins.ClearAsync(id, ct);
            return found ? Results.NoContent() : Results.NotFound();
        });
    }

    private static AgentTaskRole? ParseRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<AgentTaskRole>(value, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            throw new ValidationException("role", $"'{value}' is not a task role.");
        return role;
    }
}

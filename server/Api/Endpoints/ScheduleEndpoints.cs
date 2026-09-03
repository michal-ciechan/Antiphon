using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/schedules").WithTags("Schedules");

        group.MapGet("/", async (
            Guid? agentId,
            string? cardId,
            Guid? boardId,
            bool? enabled,
            ScheduleService schedules,
            CancellationToken ct) =>
        {
            Guid? resolvedCardId = null;
            if (!string.IsNullOrWhiteSpace(cardId))
                resolvedCardId = await schedules.ResolveCardIdForScheduleAsync(cardId, ct);
            return Results.Ok(new ScheduleListDto(
                await schedules.ListAsync(agentId, resolvedCardId, boardId, enabled, ct)));
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            ScheduleService schedules,
            CancellationToken ct) =>
            Results.Ok(await schedules.GetAsync(id, ct)));

        group.MapPost("/", async (
            CreateScheduleRequest request,
            ScheduleService schedules,
            CancellationToken ct) =>
            Results.Ok(await schedules.CreateAsync(request, ct)));

        group.MapPost("/preview", async (
            CreateScheduleRequest request,
            ScheduleService schedules,
            CancellationToken ct) =>
            Results.Ok(await schedules.PreviewRequestAsync(request, ct)));

        group.MapGet("/{id:guid}/preview", async (
            Guid id,
            ScheduleService schedules,
            CancellationToken ct) =>
            Results.Ok(await schedules.PreviewExistingAsync(id, ct)));

        group.MapPatch("/{id:guid}", async (
            Guid id,
            PatchScheduleRequest request,
            ScheduleService schedules,
            CancellationToken ct) =>
            Results.Ok(await schedules.PatchAsync(id, request, ct)));

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ScheduleService schedules,
            CancellationToken ct) =>
        {
            await schedules.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/fire-now", async (
            Guid id,
            ScheduleService schedules,
            CancellationToken ct) =>
        {
            await schedules.FireNowAsync(id, ct);
            return Results.Accepted();
        });
    }
}

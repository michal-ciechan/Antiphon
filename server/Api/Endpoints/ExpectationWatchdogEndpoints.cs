using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class ExpectationWatchdogEndpoints
{
    public static void MapExpectationWatchdogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/expectation-watchdog", async (Guid? boardId, int? skip, int? take,
            HttpContext http, AgentTaskService tasks, ExpectationWatchdogStatusService status,
            Antiphon.Server.Infrastructure.Data.AppDbContext db, CancellationToken ct) =>
        {
            if (boardId is null || boardId == Guid.Empty)
                throw new ValidationException("boardId", "boardId is required.");
            var caller = await AgentTaskEndpoints.ResolveCallerAsync(http, tasks, ct);
            if (caller.Task is not null || caller.CapabilityId is not null)
            {
                var projectId = await db.Boards.AsNoTracking().Where(b => b.Id == boardId)
                    .Select(b => (Guid?)b.ProjectId).SingleOrDefaultAsync(ct);
                var taskBoard = caller.Task?.CardId is Guid cardId
                    ? await db.Cards.AsNoTracking().Where(c => c.Id == cardId)
                        .Select(c => (Guid?)c.BoardId).SingleOrDefaultAsync(ct)
                    : null;
                if (projectId is null
                    || caller.BoardId is { } board && board != boardId
                    || caller.ProjectId is { } project && project != projectId
                    || taskBoard is { } ownedBoard && ownedBoard != boardId
                    || caller.Task is not null && taskBoard is null && caller.Task.ProjectId != projectId
                    || caller.CapabilityId is not null && caller.BoardId is null && caller.ProjectId is null)
                    throw new ForbiddenException("The task token cannot read this board.");
            }
            return Results.Ok(await status.GetAsync(boardId.Value,
                Math.Max(0, skip ?? 0), Math.Clamp(take ?? 20, 1, 100), ct));
        }).WithTags("ExpectationWatchdog");
    }
}

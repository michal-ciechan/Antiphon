using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>Component routes; registration belongs to the sequential S6 integration slice.</summary>
public static class PipelineHandoffActionEndpoints
{
    public static void MapPipelineHandoffActionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent-tasks/{predecessorId:guid}/handoff-actions", async (
            Guid predecessorId, PipelineHandoffActionRequest request, HttpContext http,
            AgentTaskService tasks, PipelineHandoffActionService actions, AppDbContext db,
            IOptions<PhoneHomeRunnerSettings> settings, CancellationToken ct) =>
        {
            var actor = await AuthorizeAsync(predecessorId, http, tasks, db, settings.Value, ct);
            return Results.Ok(await actions.RecordAsync(predecessorId, request, actor, ct));
        });
        app.MapGet("/api/agent-tasks/{predecessorId:guid}/handoff-actions", async (
            Guid predecessorId, HttpContext http, AgentTaskService tasks,
            PipelineHandoffActionService actions, AppDbContext db,
            IOptions<PhoneHomeRunnerSettings> settings, CancellationToken ct) =>
        {
            await AuthorizeAsync(predecessorId, http, tasks, db, settings.Value, ct);
            return Results.Ok(await actions.GetAsync(predecessorId, ct));
        });
    }

    private static async Task<string> AuthorizeAsync(Guid predecessorId, HttpContext http,
        AgentTaskService tasks, AppDbContext db, PhoneHomeRunnerSettings settings, CancellationToken ct)
    {
        if (OperatorCredential.HeaderMatches(http, settings)) return "operator";
        // Never use the task API's anonymous UI fallback on an accounting write/read.
        var caller = await tasks.AuthenticateAsync(http.Request.Headers["X-Antiphon-Task-Token"].FirstOrDefault(), ct);
        if (!caller.MayDelegate) throw Denied("principal");
        var predecessor = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == predecessorId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), predecessorId);
        var project = caller.Task?.ProjectId ?? await tasks.DeriveCallerProjectAsync(caller, ct);
        if (predecessor.ProjectId is null || project != predecessor.ProjectId) throw Denied("project");
        var roots = new[] { caller.Task?.RepoPath ?? caller.WorkingDirectory }.Concat(caller.ExtraAllowedRoots ?? []);
        if (predecessor.RepoPath is null || !roots.Any(root => !string.IsNullOrWhiteSpace(root)
            && DelegationWorkspaceResolver.IsWithinRoot(predecessor.RepoPath, root))) throw Denied("repository");
        var card = predecessor.CardId is { } id ? await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) : null;
        if (caller.Task?.CardId is { } callerCard && callerCard != predecessor.CardId) throw Denied("card");
        var boardId = caller.BoardId;
        if (boardId is null && caller.SessionId is { } sessionId && caller.Task is null)
        {
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct);
            if (session.CardId is { } sessionCard && sessionCard != predecessor.CardId) throw Denied("card");
            boardId = await db.Agents.AsNoTracking().Where(a => a.PersistentSessionId == sessionId.ToString("D"))
                .Select(a => a.BoardId).FirstOrDefaultAsync(ct);
        }
        if (boardId is not null && card?.BoardId != boardId) throw Denied("board");
        if (card is not null && !await db.Boards.AnyAsync(b => b.Id == card.BoardId && b.ProjectId == project, ct))
            throw Denied("card");
        if (predecessor.ParentSessionId is not { } recipient || caller.SessionId != recipient)
            throw Denied("recipient");
        return caller.Task is { } task ? $"task:{task.Id:D}"
            : caller.CapabilityId is { } capability ? $"capability:{capability:D}" : $"session:{caller.SessionId:D}";
    }

    private static ForbiddenException Denied(string scope) => new("Handoff accounting requires the caller's authorized scope.", "handoff_" + scope + "_denied");
}

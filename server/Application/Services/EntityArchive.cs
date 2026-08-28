using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Shared archive request validation and attachment guards for boards and projects
/// (CARD-0217 S9). Archive is reversible hide, not delete: a live agent, live session or
/// open task is the only reason to refuse.
/// </summary>
internal static class EntityArchive
{
    private static readonly SessionStatus[] LiveSessionStatuses =
    [
        SessionStatus.Created,
        SessionStatus.Starting,
        SessionStatus.Running,
        SessionStatus.Stopping
    ];

    private static readonly AgentTaskStatus[] OpenTaskStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
        AgentTaskStatus.Blocked
    ];

    public static void Validate(string reason, string? actor)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(reason))
            errors["Reason"] = ["A reason is required."];
        RequireWithinLimit(errors, "Reason", reason?.Trim(), CardService.MaxReasonLength);
        RequireWithinLimit(errors, "ArchivedBy", actor?.Trim(), CardService.MaxActorLength);
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    public static async Task EnsureBoardArchiveableAsync(
        AppDbContext db, Guid boardId, string boardName, CancellationToken ct)
    {
        var agentNames = await db.Agents.AsNoTracking()
            .Where(a => a.BoardId == boardId)
            .Select(a => a.Name)
            .ToListAsync(ct);
        if (agentNames.Count > 0)
        {
            throw new ConflictException(
                $"Board '{boardName}' has agent(s) attached ({string.Join(", ", agentNames)}); "
                + "detach them before archiving.");
        }

        if (await HasLiveSessionOnBoardAsync(db, boardId, ct))
        {
            throw new ConflictException(
                $"Board '{boardName}' has a live session; stop it before archiving.");
        }

        if (await HasOpenTaskOnBoardAsync(db, boardId, ct))
        {
            throw new ConflictException(
                $"Board '{boardName}' has a non-terminal task; settle or cancel it before archiving.");
        }
    }

    public static async Task EnsureProjectArchiveableAsync(
        AppDbContext db, Guid projectId, string projectName, CancellationToken ct)
    {
        var agentNames = await db.Agents.AsNoTracking()
            .Where(a => a.PoolProjectId == projectId
                || (a.BoardId != null && db.Boards.Any(b => b.Id == a.BoardId && b.ProjectId == projectId)))
            .Select(a => a.Name)
            .ToListAsync(ct);
        if (agentNames.Count > 0)
        {
            throw new ConflictException(
                $"Project '{projectName}' has agent(s) attached ({string.Join(", ", agentNames)}); "
                + "detach them before archiving.");
        }

        var boardIds = await db.Boards.AsNoTracking()
            .Where(b => b.ProjectId == projectId)
            .Select(b => b.Id)
            .ToListAsync(ct);
        foreach (var boardId in boardIds)
        {
            if (await HasLiveSessionOnBoardAsync(db, boardId, ct))
            {
                throw new ConflictException(
                    $"Project '{projectName}' has a live session; stop it before archiving.");
            }

            if (await HasOpenTaskOnBoardAsync(db, boardId, ct))
            {
                throw new ConflictException(
                    $"Project '{projectName}' has a non-terminal task; settle or cancel it before archiving.");
            }
        }

        var openOnProject = await db.AgentTasks.AsNoTracking()
            .AnyAsync(t => t.ProjectId == projectId && OpenTaskStatuses.Contains(t.Status), ct);
        if (openOnProject)
        {
            throw new ConflictException(
                $"Project '{projectName}' has a non-terminal task; settle or cancel it before archiving.");
        }
    }

    private static async Task<bool> HasLiveSessionOnBoardAsync(
        AppDbContext db, Guid boardId, CancellationToken ct)
    {
        var liveOnCards = await db.AgentSessions.AsNoTracking()
            .AnyAsync(
                s => s.CardId != null
                    && LiveSessionStatuses.Contains(s.Status)
                    && db.Cards.Any(c => c.Id == s.CardId && c.BoardId == boardId),
                ct);
        if (liveOnCards)
            return true;

        var persistentIds = await db.Agents.AsNoTracking()
            .Where(a => a.BoardId == boardId && a.PersistentSessionId != null)
            .Select(a => a.PersistentSessionId!)
            .ToListAsync(ct);
        var sessionIds = ParseGuids(persistentIds);
        if (sessionIds.Count == 0)
            return false;

        return await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => sessionIds.Contains(s.Id) && LiveSessionStatuses.Contains(s.Status), ct);
    }

    private static Task<bool> HasOpenTaskOnBoardAsync(
        AppDbContext db, Guid boardId, CancellationToken ct) =>
        db.AgentTasks.AsNoTracking()
            .AnyAsync(
                t => OpenTaskStatuses.Contains(t.Status)
                    && t.CardId != null
                    && db.Cards.Any(c => c.Id == t.CardId && c.BoardId == boardId),
                ct);

    private static List<Guid> ParseGuids(IEnumerable<string> values)
    {
        var ids = new List<Guid>();
        foreach (var value in values)
        {
            if (Guid.TryParse(value, out var id))
                ids.Add(id);
        }

        return ids;
    }

    private static void RequireWithinLimit(
        Dictionary<string, string[]> errors, string field, string? value, int limit)
    {
        if (value is null || value.Length <= limit)
            return;

        errors[field] = [$"{field} must be at most {limit:N0} characters; got {value.Length:N0}."];
    }
}

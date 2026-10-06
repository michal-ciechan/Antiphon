using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The latest task bound to a session, plus the open Dispatched/Working task when that is a different row.</summary>
internal sealed record SeatBoundTask(
    Guid Id,
    AgentTaskStatus Status,
    int Attempt,
    AgentTaskRole Role,
    Guid? CardId,
    Guid? BoardId,
    Guid? AgentId,
    DateTime? CompletedAt);

/// <summary>
/// Desktop facts for one runner session. <see cref="OpenTaskId"/> is set only for Dispatched
/// or Working. <see cref="LatestTask"/> is the newest task on the session regardless of status.
/// </summary>
internal readonly record struct SeatDesktopRow(
    bool Live,
    string? Status,
    Guid? OpenTaskId,
    SeatBoundTask? LatestTask,
    DateTime? BlockedAt,
    bool PooledWarm,
    bool PublicationReceipt);

/// <summary>
/// CARD-1079: the one desktop join shared by the slots route and the occupancy sampler.
/// A seat is owned only by a Dispatched or Working task. One query per table, and the card
/// query reads the board id only.
/// </summary>
internal static class SeatDesktopJoin
{
    internal static async Task<Dictionary<Guid, SeatDesktopRow>> LoadAsync(
        AppDbContext db, Guid[] sessionIds, CancellationToken ct)
    {
        var rows = new Dictionary<Guid, SeatDesktopRow>(sessionIds.Length);
        if (sessionIds.Length == 0)
            return rows;

        var sessions = await db.AgentSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Status })
            .ToListAsync(ct);
        var tasks = await db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId != null && sessionIds.Contains(t.AgentSessionId.Value))
            .Select(t => new TaskSlice(
                t.Id,
                t.AgentSessionId!.Value,
                t.Status,
                t.Attempt,
                t.Role,
                t.CardId,
                t.AgentId,
                t.CompletedAt,
                t.CreatedAt))
            .ToListAsync(ct);

        var latestBySession = new Dictionary<Guid, TaskSlice>();
        var openBySession = new Dictionary<Guid, TaskSlice>();
        foreach (var task in tasks)
        {
            if (!latestBySession.TryGetValue(task.SessionId, out var current) || Newer(task, current))
                latestBySession[task.SessionId] = task;
            if (task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working
                && (!openBySession.TryGetValue(task.SessionId, out var open) || Newer(task, open)))
                openBySession[task.SessionId] = task;
        }

        var latestIds = latestBySession.Values.Select(t => t.Id).ToArray();
        var blockedAt = new Dictionary<Guid, DateTime>();
        if (latestIds.Length > 0)
        {
            var blocked = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => latestIds.Contains(e.AgentTaskId) && e.Type == AgentTaskEventType.Blocked)
                .Select(e => new { e.AgentTaskId, e.At })
                .ToListAsync(ct);
            foreach (var group in blocked.GroupBy(e => e.AgentTaskId))
                blockedAt[group.Key] = group.Max(e => e.At);
        }

        var cardIds = latestBySession.Values
            .Select(t => t.CardId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var boards = cardIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Cards.AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .Select(c => new { c.Id, c.BoardId })
                .ToDictionaryAsync(c => c.Id, c => c.BoardId, ct);

        var keys = sessionIds.Select(id => id.ToString("D")).ToArray();
        var pooled = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null
                && keys.Contains(a.PersistentSessionId)
                && a.Status == AgentStatus.Idle
                && a.PoolIdleSince != null)
            .Select(a => a.PersistentSessionId!)
            .ToListAsync(ct);
        var pooledIds = pooled
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var receipts = new HashSet<(Guid TaskId, int Attempt)>();
        if (latestIds.Length > 0)
        {
            var parks = await db.AgentTaskParks.AsNoTracking()
                .Where(p => latestIds.Contains(p.TaskId) && p.PublicationReceiptId != null)
                .Select(p => new { p.TaskId, p.Attempt })
                .ToListAsync(ct);
            foreach (var park in parks)
                receipts.Add((park.TaskId, park.Attempt));
        }

        var sessionById = sessions.ToDictionary(s => s.Id);
        foreach (var id in sessionIds)
        {
            sessionById.TryGetValue(id, out var session);
            var live = session is not null
                && session.Status is SessionStatus.Created or SessionStatus.Starting
                    or SessionStatus.Running or SessionStatus.Stopping;
            latestBySession.TryGetValue(id, out var latest);
            openBySession.TryGetValue(id, out var open);
            SeatBoundTask? bound = null;
            DateTime? blockedTime = null;
            var hasReceipt = false;
            if (latest is not null)
            {
                Guid? boardId = latest.CardId is Guid cardId && boards.TryGetValue(cardId, out var board)
                    ? board
                    : null;
                bound = new SeatBoundTask(
                    latest.Id,
                    latest.Status,
                    latest.Attempt,
                    latest.Role,
                    latest.CardId,
                    boardId,
                    latest.AgentId,
                    latest.CompletedAt);
                if (blockedAt.TryGetValue(latest.Id, out var at))
                    blockedTime = at;
                hasReceipt = receipts.Contains((latest.Id, latest.Attempt));
            }

            rows[id] = new SeatDesktopRow(
                live,
                session?.Status.ToString(),
                open?.Id,
                bound,
                blockedTime,
                pooledIds.Contains(id),
                hasReceipt);
        }

        return rows;
    }

    private static bool Newer(TaskSlice candidate, TaskSlice current) =>
        candidate.CreatedAt > current.CreatedAt
        || (candidate.CreatedAt == current.CreatedAt && candidate.Id.CompareTo(current.Id) > 0);

    private sealed record TaskSlice(
        Guid Id,
        Guid SessionId,
        AgentTaskStatus Status,
        int Attempt,
        AgentTaskRole Role,
        Guid? CardId,
        Guid? AgentId,
        DateTime? CompletedAt,
        DateTime CreatedAt);
}

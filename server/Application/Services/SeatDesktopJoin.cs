using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The latest task bound to a session, plus the owner task (Queued, Dispatched, Working or Blocked) when that is a different row.</summary>
internal sealed record SeatBoundTask(
    Guid Id,
    AgentTaskStatus Status,
    int Attempt,
    AgentTaskRole Role,
    Guid? CardId,
    Guid? BoardId,
    Guid? AgentId,
    DateTime? CompletedAt);

/// <summary>The owner's current-attempt park episode, or null when that attempt has no park row.</summary>
internal sealed record SeatParkRow(
    Guid ParkId,
    AgentTaskParkState State,
    string ReasonCode,
    Guid? ReleaseId,
    AgentTaskParkSyncState SyncState);

/// <summary>
/// Desktop facts for one runner session. <see cref="OpenTaskId"/> is the owner task: the latest
/// Queued, Dispatched, Working or Blocked task bound to the session. <see cref="LatestTask"/> is
/// the newest task on the session regardless of status.
/// </summary>
internal readonly record struct SeatDesktopRow(
    bool Live,
    string? Status,
    Guid? OpenTaskId,
    SeatBoundTask? LatestTask,
    DateTime? BlockedAt,
    bool PooledWarm,
    bool PublicationReceipt,
    SeatParkRow? Park = null);

/// <summary>
/// CARD-1079: the one desktop join shared by the slots route and the occupancy sampler.
/// A seat is owned by the latest Queued, Dispatched, Working or Blocked task (CARD-1124).
/// The task table is the latest row per session (CARD-1090), not that session's whole history.
/// The card query reads the board id only. One park query feeds both facts in memory.
/// <see cref="SeatDesktopRow.Park"/> is the owner task's current-attempt episode.
/// <see cref="SeatDesktopRow.PublicationReceipt"/> follows the latest task's current attempt,
/// which is the task the seat observation prints: a seat with no owner still reports that
/// receipt, so a settled published orphan reads pushed=yes (CARD-1079; CARD-1124 S2).
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
        var scoped = db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId != null && sessionIds.Contains(t.AgentSessionId.Value));
        var latestBySession = await LatestPerSessionAsync(scoped, ct);
        var openBySession = await LatestPerSessionAsync(
            scoped.Where(t => t.Status == AgentTaskStatus.Queued
                || t.Status == AgentTaskStatus.Dispatched
                || t.Status == AgentTaskStatus.Working
                || t.Status == AgentTaskStatus.Blocked),
            ct);

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

        var parkRows = new List<ParkSlice>();
        if (latestIds.Length > 0)
        {
            var parkTaskIds = latestIds
                .Concat(openBySession.Values.Select(t => t.Id))
                .Distinct()
                .ToArray();
            parkRows = await db.AgentTaskParks.AsNoTracking()
                .Where(p => parkTaskIds.Contains(p.TaskId))
                .Select(p => new ParkSlice(
                    p.Id,
                    p.TaskId,
                    p.Attempt,
                    p.State,
                    p.ReasonCode,
                    p.RunnerSeatReleaseId,
                    p.SyncState,
                    p.CreatedAt,
                    p.PublicationReceiptId != null))
                .ToListAsync(ct);
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
            }

            var hasReceipt = false;
            if (latest is not null)
            {
                var receiptTaskId = latest.Id;
                var receiptAttempt = latest.Attempt;
                hasReceipt = parkRows.Any(p =>
                    p.TaskId == receiptTaskId && p.Attempt == receiptAttempt && p.HasReceipt);
            }
            rows[id] = new SeatDesktopRow(
                live,
                session?.Status.ToString(),
                open?.Id,
                bound,
                blockedTime,
                pooledIds.Contains(id),
                hasReceipt,
                CurrentPark(parkRows, open));
        }

        return rows;
    }

    private static async Task<Dictionary<Guid, TaskSlice>> LatestPerSessionAsync(
        IQueryable<AgentTask> tasks, CancellationToken ct)
    {
        var peaks = await tasks
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { SessionId = g.Key, CreatedAt = g.Max(t => t.CreatedAt) })
            .ToListAsync(ct);
        var wanted = new Dictionary<Guid, DateTime>();
        foreach (var peak in peaks)
        {
            if (peak.SessionId is Guid sessionId)
                wanted[sessionId] = peak.CreatedAt;
        }

        if (wanted.Count == 0)
            return new Dictionary<Guid, TaskSlice>();

        var ids = wanted.Keys.ToArray();
        var stamps = wanted.Values.Distinct().ToArray();
        var candidates = await tasks
            .Where(t => t.AgentSessionId != null
                && ids.Contains(t.AgentSessionId.Value)
                && stamps.Contains(t.CreatedAt))
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

        var latest = new Dictionary<Guid, TaskSlice>();
        foreach (var task in candidates)
        {
            if (!wanted.TryGetValue(task.SessionId, out var createdAt) || task.CreatedAt != createdAt)
                continue;
            if (!latest.TryGetValue(task.SessionId, out var current) || Newer(task, current))
                latest[task.SessionId] = task;
        }

        return latest;
    }

    private static bool Newer(TaskSlice candidate, TaskSlice current) =>
        candidate.CreatedAt > current.CreatedAt
        || (candidate.CreatedAt == current.CreatedAt && candidate.Id.CompareTo(current.Id) > 0);

    private static SeatParkRow? CurrentPark(List<ParkSlice> parks, TaskSlice? owner)
    {
        if (owner is null)
            return null;
        ParkSlice? chosen = null;
        foreach (var park in parks)
        {
            if (park.TaskId != owner.Id || park.Attempt != owner.Attempt)
                continue;
            if (chosen is null
                || park.CreatedAt > chosen.CreatedAt
                || (park.CreatedAt == chosen.CreatedAt && park.Id.CompareTo(chosen.Id) > 0))
                chosen = park;
        }

        return chosen is null
            ? null
            : new SeatParkRow(chosen.Id, chosen.State, chosen.ReasonCode, chosen.ReleaseId, chosen.SyncState);
    }

    private sealed record ParkSlice(
        Guid Id,
        Guid TaskId,
        int Attempt,
        AgentTaskParkState State,
        string ReasonCode,
        Guid? ReleaseId,
        AgentTaskParkSyncState SyncState,
        DateTime CreatedAt,
        bool HasReceipt);

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

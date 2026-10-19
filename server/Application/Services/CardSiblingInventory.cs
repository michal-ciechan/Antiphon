using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Bounded discovery; pending requests are queried independently of the sampled rows.</summary>
public sealed class CardSiblingInventory(AppDbContext db)
{
    public sealed record Pending(AgentTask Task, AgentTaskLandRequest Request, string? OriginalSourceSha);
    public sealed record Snapshot(
        IReadOnlyList<AgentTask> Rows, IReadOnlyList<Pending> PendingLands,
        IReadOnlySet<Guid> Landed, IReadOnlyDictionary<Guid, AgentTaskLandRequest> Requests,
        bool Overflow, bool PendingOverflow);

    public async Task<Snapshot> ReadAsync(AgentTask task, bool warningsOnly,
        WorktreeBaseInspection inspection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var query = db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
            && t.CardId == task.CardId && t.Workspace == WorkspaceMode.Worktree
            && t.WorktreeBranch != null);
        if (warningsOnly)
            query = query.Where(t => t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Blocked);
        // This join deliberately does not depend on the sampled IDs or on branch deduplication.
        var pendingQuery = from t in query
            join r in db.AgentTaskLandRequests.AsNoTracking() on t.CurrentLandRequestId equals (Guid?)r.Id
            where r.IsPending && (r.State == LandRequestState.Queued
                || r.State == LandRequestState.Held || r.State == LandRequestState.Running)
            orderby t.Id
            select new { Task = t, Request = r };
        var pending = await inspection.ReadAsync(token => pendingQuery
            .Take(inspection.MaxInventoryRows + 1).ToListAsync(token));
        var operations = pending.Select(p => p.Request.LandingOperationId).OfType<Guid>().ToArray();
        var originals = await inspection.ReadAsync(token => db.AgentTaskLandings.AsNoTracking()
            .Where(o => operations.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OriginalSourceSha, token));
        var rows = await inspection.ReadAsync(token => query.OrderBy(t => t.Id)
            .Take(inspection.MaxInventoryRows + 1).ToListAsync(token));
        var ids = rows.Select(t => t.Id).ToArray();
        var requestIds = rows.Select(t => t.CurrentLandRequestId).OfType<Guid>().ToArray();
        var requests = await inspection.ReadAsync(token => db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, token));
        var landed = await inspection.ReadAsync(token => db.AgentTaskEvents.AsNoTracking()
            .Where(e => ids.Contains(e.AgentTaskId)
                && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.LandedWithResidue))
            .Select(e => e.AgentTaskId).Distinct().ToListAsync(token));
        return new Snapshot(rows, pending.Select(p => new Pending(p.Task, p.Request,
                p.Request.LandingOperationId is Guid id ? originals.GetValueOrDefault(id) : null)).ToArray(),
            landed.ToHashSet(), requests, rows.Count > inspection.MaxInventoryRows,
            pending.Count > inspection.MaxInventoryRows);
    }
}

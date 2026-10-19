using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>Records current-attempt readiness without discarding late resource evidence.</summary>
public static class RemoteWorkspaceReadiness
{
    public const string ReadyHold = "Held: prepared workspace is ready; awaiting dispatch admission.";

    public static bool IsInFlight(string? detail) =>
        detail?.StartsWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix, StringComparison.Ordinal) == true;

    public static string CurrentDetail(AgentTaskStatus status, string? remotePath, string detail) =>
        status == AgentTaskStatus.Queued && !string.IsNullOrWhiteSpace(remotePath) && IsInFlight(detail)
            ? ReadyHold : detail;

    public static async Task<bool> RecordPathAsync(AppDbContext db, Guid taskId, int attempt,
        string? runnerId, string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || string.IsNullOrWhiteSpace(path)) return false;
        await using var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var task = await db.AgentTasks.FromSqlInterpolated(
            $"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (task is null || task.Attempt != attempt || task.RunnerId != runnerId
            || !string.IsNullOrWhiteSpace(task.RemoteWorktreePath) && task.RemoteWorktreePath != path)
            return false;
        if (task.Status is not (AgentTaskStatus.Queued or AgentTaskStatus.Canceled)) return false;
        // A late canceled attempt may still own the path; it gains no ready transition.
        await db.AgentTasks.Where(t => t.Id == taskId && t.Attempt == attempt && t.RunnerId == runnerId)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RemoteWorktreePath, path)
                .SetProperty(t => t.RemotePrepFailures, 0)
                .SetProperty(t => t.DispatchNotBeforeAt, (DateTime?)null), ct);
        var latest = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && (e.Type == AgentTaskEventType.Held || e.Type == AgentTaskEventType.Dispatched))
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct);
        if (task.Status == AgentTaskStatus.Queued && latest?.Type == AgentTaskEventType.Held && IsInFlight(latest.Detail))
        {
            var now = DateTime.UtcNow;
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Held, Detail = ReadyHold,
                At = now > latest.At ? now : latest.At.AddTicks(10),
            });
            await db.SaveChangesAsync(ct);
        }
        if (owned is not null) await owned.CommitAsync(ct);
        return true;
    }

    /// <summary>The hint has its own failure boundary, after the capturing transaction commits.</summary>
    public static async Task PublishAsync(AppDbContext db, Guid taskId, int attempt, string? runnerId,
        IEventBus events, ILogger logger, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return;
        try
        {
            var ready = await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId
                && t.Attempt == attempt && t.RunnerId == runnerId && t.Status == AgentTaskStatus.Queued
                && t.RemoteWorktreePath != null && t.RemoteWorktreePath != "")
                .Select(t => new { t.Id, t.RootTaskId }).SingleOrDefaultAsync(ct);
            if (ready is not null)
                await events.PublishToAllAsync("AgentTaskChanged", new { taskId = ready.Id, rootId = ready.RootTaskId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Prepared workspace notification failed for task {TaskId} attempt {Attempt}", taskId, attempt);
        }
    }
}

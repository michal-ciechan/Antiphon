using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTaskLandMonitorService(AppDbContext db, TimeProvider clock,
    IOptions<DelegationSettings> settings, IEventBus events)
{
    public async Task SweepAsync(CancellationToken ct)
    {
        var ids = await db.AgentTaskLandRequests.AsNoTracking().Where(r => r.IsPending)
            .Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == id, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {request.TaskId} FOR UPDATE", ct);
            await db.Entry(request).ReloadAsync(ct);
            if (!request.IsPending) continue;
            var now = clock.GetUtcNow().UtcDateTime;
            request.LastEvaluatedAt = now;
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == request.TaskId, ct);
            request.ReconciliationError = task.CurrentLandRequestId != request.Id || task.LandRequestedAt != request.RequestedAt
                || task.LandAttempt != request.Attempt ? "land_request_mirror_disagreement" : null;
            var age = (now - request.LastProgressAt).TotalSeconds;
            var changed = false;
            if (age >= settings.Value.LandWarningSeconds && request.WarningAt is null)
            { request.WarningAt = now; AddAged(request, "Warning", now); changed = true; }
            if (age >= settings.Value.LandErrorSeconds && request.ErrorAt is null)
            { request.ErrorAt = now; AddAged(request, "Error", now); changed = true; }
            request.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (changed) await events.PublishToAllAsync("AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        }
    }

    private void AddAged(AgentTaskLandRequest request, string severity, DateTime now)
    {
        var source = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = request.TaskId, LandRequestId = request.Id,
            Type = AgentTaskEventType.LandAged, At = now,
            Detail = $"{severity}: Land {request.State}; requested {request.RequestedAt:O}; no progress since {request.LastProgressAt:O}; "
                + $"attempt={request.Attempt}; reason={request.HoldReasonCode}; holder={request.HoldingTaskId:N} ({request.HoldingTaskStatus}).",
        };
        db.AgentTaskEvents.Add(source);
        db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, source, LandNotificationKind.Aged));
    }
}

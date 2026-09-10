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
        // Publication is terminal, receipt is not. Its clock starts at the outcome commit.
        var outcomes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => !n.IsLegacy && n.Kind == LandNotificationKind.Outcome
            && n.ConfirmedAt == null && n.State != LandNotificationState.NotRequired && n.ErrorAt == null)
            .Select(n => new { n.Id, n.TaskId }).ToListAsync(ct);
        foreach (var outcome in outcomes)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {outcome.TaskId} FOR UPDATE", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTaskLandNotifications\" WHERE \"Id\" = {outcome.Id} FOR UPDATE", ct);
            var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == outcome.Id, ct);
            await db.Entry(note).ReloadAsync(ct);
            if (note.ConfirmedAt is not null || note.State == LandNotificationState.NotRequired) continue;
            var now = clock.GetUtcNow().UtcDateTime;
            var age = (now - note.CreatedAt).TotalSeconds;
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == note.RequestId, ct);
            var changed = false;
            if (age >= settings.Value.LandWarningSeconds && note.WarningAt is null)
            { note.WarningAt = now; AddReceiptAged(request, note, "Warning", now); changed = true; }
            if (age >= settings.Value.LandErrorSeconds && note.ErrorAt is null)
            { note.ErrorAt = now; AddReceiptAged(request, note, "Error", now); changed = true; }
            if (!changed) continue;
            note.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var rootId = await db.AgentTasks.Where(t => t.Id == outcome.TaskId).Select(t => t.RootTaskId).SingleAsync(ct);
            await events.PublishToAllAsync("AgentTaskChanged", new { taskId = outcome.TaskId, rootId }, ct);
        }
    }

    private void AddReceiptAged(AgentTaskLandRequest request, AgentTaskLandNotification note, string severity, DateTime now)
    {
        var source = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = request.TaskId, LandRequestId = request.Id,
            Type = AgentTaskEventType.LandAged, At = now,
            Detail = $"{severity}: outcome receipt unconfirmed; notification={note.Id:N}; outcome committed={note.CreatedAt:O}; destination={note.ParentSessionId:N}; queue={note.QueueMessageId:N}; state={note.State}; error={note.LastErrorCode}." };
        db.AgentTaskEvents.Add(source);
        db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, source, LandNotificationKind.Aged));
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

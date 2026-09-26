using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Keep an unresolved land visible after its merge helper ends.</summary>
internal static class MergeHelperOutcome
{
    public static async Task RecordUnresolvedAsync(AppDbContext db, AgentTask helper, DateTime now,
        CancellationToken ct)
    {
        if (helper.Role != AgentTaskRole.Merge || helper.ParentTaskId is not Guid parentId
            || helper.Status is not (AgentTaskStatus.Failed or AgentTaskStatus.Canceled)) return;
        var owner = await db.AgentTasks.SingleOrDefaultAsync(t => t.Id == parentId
            && t.Status == AgentTaskStatus.Blocked && t.CurrentLandRequestId != null, ct);
        if (owner?.CurrentLandRequestId is not Guid requestId) return;
        var request = await db.AgentTaskLandRequests.SingleOrDefaultAsync(r => r.Id == requestId
            && r.IsPending && r.State == LandRequestState.NeedsResolution, ct);
        if (request is null) return;

        var detail = $"Merge helper {DelegationReportFormatter.Short(helper.Id)} ended {helper.Status}; "
            + $"land request {request.Id:N} stays NeedsResolution. Resolve with -Land "
            + $"{DelegationReportFormatter.Short(owner.Id)} -ExpectedSourceSha <sha> "
            + "-FromTask <task>, retry the helper, or cancel the owner.";
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = owner.Id, Type = AgentTaskEventType.Warning,
            Detail = detail, At = now,
        });
        var conflict = new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = owner.Id, Type = AgentTaskEventType.Conflicted,
            Detail = detail, At = now, LandRequestId = request.Id,
        };
        db.AgentTaskEvents.Add(conflict);
        db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, conflict,
            LandNotificationKind.Conflict));
    }
}

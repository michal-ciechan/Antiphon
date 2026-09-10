using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed record LandCompletionFacts(string Publication, string Cleanup, string Land, string Receipt)
{
    public static async Task<LandCompletionFacts?> LoadAsync(AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (task.Workspace != WorkspaceMode.Worktree) return null;
        var operation = task.ActiveLandingId is Guid op ? await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == op, ct) : null;
        var request = task.CurrentLandRequestId is Guid id ? await db.AgentTaskLandRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) : null;
        var note = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == task.Id && n.Kind == LandNotificationKind.Outcome)
            .OrderByDescending(n => n.CreatedAt).FirstOrDefaultAsync(ct);
        return new(operation?.Publication.ToString() ?? "Unconfirmed", operation?.Cleanup.ToString() ?? "NotStarted",
            request?.State.ToString() ?? (task.LandRequestedAt is null ? "NotRequested" : "Pending"),
            note?.State.ToString() ?? "Unverified");
    }
}

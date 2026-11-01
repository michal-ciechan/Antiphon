using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    private async Task<bool> SupervisionInputCurrentAsync(AppDbContext db, SessionQueuedMessage message, CancellationToken ct)
    {
        if (message.SourceTaskSupervisionNoticeId is not { } id) return true;
        var notice = await db.TaskSupervisionNotices.SingleOrDefaultAsync(n => n.Id == id, ct);
        if (notice is not null && await TaskSupervisionNoticeService.CanSendAsync(db, notice, ct))
            return !await ReadWorkingAsync(db, message.AgentSessionId, ct);
        if (notice is null || notice.State is TaskSupervisionNoticeState.Superseded or TaskSupervisionNoticeState.NotApplicable)
        {
            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = UtcNow();
        }
        await db.SaveChangesAsync(ct);
        return false;
    }
}

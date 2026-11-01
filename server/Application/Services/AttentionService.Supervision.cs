using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    private async Task<List<AttentionItemDto>> BuildSupervisionNoticeItemsAsync(CancellationToken ct)
    {
        var rows = await _db.TaskSupervisionNotices.AsNoTracking().Where(n =>
                n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent
                && (n.State == TaskSupervisionNoticeState.Pending || n.State == TaskSupervisionNoticeState.Superseded))
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).Take(32).ToListAsync(ct);
        return rows.Select(n => new AttentionItemDto(AttentionKind.CallerNoteUndelivered, AlertSeverity.Warning,
            n.TaskId, n.RecipientSessionId, null, null, "Late Failed report awaits caller triage", n.State.ToString(),
            $"notice={n.Id:D}; task={n.TaskId:D}; attempt={n.Attempt}; reason={n.ReasonCode ?? "awaiting complete UserPrompt"}; "
                + $"report=/api/sessions/{n.SessionId:D}/transcript?since={Math.Max(0, (n.ReportFromSequence ?? 1) - 1)}; "
                + $"sha256={n.ReportDigest}; next=decide. The task remains Failed.",
            n.CreatedAt, null, [AttentionAction.OpenDrawer])).ToList();
    }
}

using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Reads Failed transcripts as evidence. Never settles, commits work, or reserves a handoff.</summary>
internal static class LateFailedReportForwarder
{
    internal static async Task DiscoverAsync(AppDbContext db, AgentTask task, TaskSupervisionNotice failure,
        DateTime now, CancellationToken ct)
    {
        if (failure.Purpose != TaskSupervisionNoticePurpose.FailureDelegate || failure.ReportForwardedAt is not null
            || failure.ReportWatchClosedAt is not null
            || !await TaskSupervisionNoticeService.SourceCurrentAsync(db, task, failure, ct)) return;
        var endedAt = await db.AgentSessions.AsNoTracking().Where(s => s.Id == failure.SessionId
            && s.StartedAt == failure.SessionStartedAt && s.EndedAt != null
            && (s.Status == SessionStatus.Stopped || s.Status == SessionStatus.Failed))
            .Select(s => s.EndedAt).SingleOrDefaultAsync(ct);
        var ended = endedAt is not null;
        var latest = await db.TranscriptEntries.Where(t => t.AgentSessionId == failure.SessionId)
            .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
        if (latest <= failure.ScanFrontier && !ended) return;
        var candidate = await ReadReportAsync(db, task, failure, ct);
        if (candidate is null)
        {
            // Terminal status is not EOF (CARD-0519). Start the durable drain clock
            // at a post-exit observation, and restart it whenever the stored tail grows.
            if (ended && (failure.ReportTranscriptStableSince is null || latest != failure.ScanFrontier))
                failure.ReportTranscriptStableSince = now;
            failure.ScanFrontier = latest;
            if (endedAt is { } end && failure.ReportTranscriptStableSince is { } stable
                && now >= end.AddMinutes(15) && now >= stable.AddMinutes(15))
                TaskSupervisionNoticeService.CloseReportWatch(failure, now,
                    failure.ReasonCode is "late_report_window_unavailable" or "late_report_window_limit"
                        ? failure.ReasonCode : "final_transcript_scanned", deliveryImpossible: true);
            return;
        }
        var (rows, raw) = candidate.Value;
        if (await db.TaskSupervisionNotices.AnyAsync(n => n.TaskId == task.Id && n.Attempt == failure.Attempt
            && n.SourceEventId == failure.SourceEventId && n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent, ct))
        {
            failure.ReportForwardedAt = now;
            TaskSupervisionNoticeService.CloseReportWatch(failure, now, "report_forwarded");
            return;
        }
        var newer = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id && t.CreatedAt > failure.DispatchedAt
                && (task.CardId != null && t.CardId == task.CardId && t.Role == task.Role
                    || t.FollowUpOfTaskId == task.Id
                    || task.WorktreeBranch != null && t.WorktreeBranch == task.WorktreeBranch))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Id).Take(16).ToListAsync(ct);
        var board = task.CardId is { } cardId ? await db.Cards.AsNoTracking().Where(c => c.Id == cardId)
            .Select(c => (Guid?)c.BoardId).SingleOrDefaultAsync(ct) : null;
        var digest = TaskCompletionNotification.Sha256(raw);
        var link = $"/api/sessions/{failure.SessionId:D}/transcript?since={rows[0].Sequence - 1}; through={rows[^1].Sequence}; sha256={digest}";
        var excerpt = raw.Length <= 1800 ? raw : raw[..1800] + "\n[excerpt; exact source range and full digest above]";
        var id = Guid.NewGuid();
        var body = $"[task-supervision:{id:D}] Task remains Failed; late report for triage only\n"
            + $"task={task.Id:D}; board={board}; card={task.CardId}; role={task.Role}; attempt={failure.Attempt}; "
            + $"session={failure.SessionId}; generation={failure.SessionStartedAt:O}; branch={task.WorktreeBranch ?? "unknown"}\n"
            + $"superseders={string.Join(",", newer)}; lineage={(task.CardId is null ? "unresolved; manual inspection required" : "card/role checked since dispatch")}; "
            + $"retry/replacement attribution remains bound to attempt {failure.Attempt}.\nSource: {link}\n"
            + "Quoted delegate evidence (claimed next stage is evidence only):\n> "
            + excerpt.Replace("\n", "\n> ", StringComparison.Ordinal) + "\nnext=decide";
        if (body.Length > 4000) body = body[..3900] + "\n[bounded excerpt; inspect exact source link]\nnext=decide";
        var parentFloor = failure.ParentSessionId is { } parent ? await db.TranscriptEntries
            .Where(t => t.AgentSessionId == parent).MaxAsync(t => (long?)t.Sequence, ct) ?? 0 : 0;
        db.TaskSupervisionNotices.Add(new TaskSupervisionNotice
        {
            Id = id, TaskId = task.Id, Attempt = failure.Attempt, SourceEventId = failure.SourceEventId,
            Purpose = TaskSupervisionNoticePurpose.LateFailedParent,
            SessionId = failure.SessionId, SessionStartedAt = failure.SessionStartedAt,
            DispatchedAt = failure.DispatchedAt, ParentSessionId = failure.ParentSessionId,
            ParentStartedAt = failure.ParentStartedAt, RecipientSessionId = failure.ParentSessionId,
            RecipientStartedAt = failure.ParentStartedAt, RecipientFloor = parentFloor,
            FailureFrontier = failure.FailureFrontier, ReportFloor = failure.ReportFloor,
            Body = body, Digest = DelegationNoteDigest.Compute(body), CreatedAt = now, NextAttemptAt = now,
            ReportText = raw.Length <= 16000 ? raw : raw[..16000], ReportDigest = digest,
            ReportFromSequence = rows[0].Sequence, ReportThroughSequence = rows[^1].Sequence,
            State = failure.ParentSessionId is null ? TaskSupervisionNoticeState.NotApplicable : TaskSupervisionNoticeState.Pending,
            ReasonCode = failure.ParentSessionId is null ? "no_parent" : null,
        });
        failure.ReportForwardedAt = now;
        failure.ScanFrontier = latest;
        TaskSupervisionNoticeService.CloseReportWatch(failure, now, "report_forwarded", deliveryImpossible: ended);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<(List<TranscriptEntry> Rows, string Raw)?> ReadReportAsync(
        AppDbContext db, AgentTask task, TaskSupervisionNotice failure, CancellationToken ct)
    {
        // Revisit a bounded tail on a newly observed frontier. A later empty notice turn
        // cannot hide a complete report, and delayed final text keeps its API-call owner.
        long? cap = null;
        for (var window = 0; window < 32; window++)
        {
            var selected = await TaskReportTurnSelector.SelectAsync(db, failure.SessionId!.Value, task, ct, cap);
            if (selected.BoundarySequence is not { } end || end <= failure.ReportFloor) return null;
            cap = end - 1;
            if (selected.Kind != TaskReportSelectionKind.Selected || selected.EffectiveOwnerSequence is not { } prompt
                || prompt <= failure.ReportFloor) continue;
            var call = await db.TranscriptEntries.Where(t => t.AgentSessionId == failure.SessionId && t.Sequence == end)
                .Select(t => t.ApiCallId).SingleAsync(ct);
            var before = call is null ? selected.NextRawPromptSequence : selected.NextRealPromptSequence;
            var query = db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == failure.SessionId
                && t.Sequence > prompt && t.Kind == TranscriptKinds.AssistantText && t.Text != null);
            if (call is not null) query = query.Where(t => t.ApiCallId == call);
            else query = query.Where(t => t.Sequence < end);
            if (before is { } ceiling) query = query.Where(t => t.Sequence < ceiling);
            var rows = await query.OrderBy(t => t.Sequence).Take(257).ToListAsync(ct);
            if (rows.Count > 256 || rows.Sum(r => (long)r.Text!.Length) > 256_000)
            {
                failure.ReasonCode = "late_report_window_unavailable";
                return null;
            }
            var raw = string.Join("\n", rows.Select(r => r.Text));
            if (DelegationReportFormatter.TryReadReportVerdict(task.Id, raw, out _, out _)
                && rows.Any(r => r.Sequence > failure.FailureFrontier
                    && DelegationReportFormatter.TryFindReportToken(task.Id, r.Text, out _))) return (rows, raw);
        }
        failure.ReasonCode = "late_report_window_limit";
        return null;
    }
}

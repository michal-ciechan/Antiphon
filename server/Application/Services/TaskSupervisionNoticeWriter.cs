using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The failure transaction owns this debt, independently of caller completion.</summary>
internal static class TaskSupervisionNoticeWriter
{
    internal static async Task RecordFailureAsync(AppDbContext db, AgentTask task,
        AgentTaskEvent failed, DateTime now, bool historical, CancellationToken ct)
    {
        if (db.TaskSupervisionNotices.Local.Any(n => n.TaskId == task.Id && n.Attempt == task.Attempt
                && n.SourceEventId == failed.Id && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate)
            || await db.TaskSupervisionNotices.AnyAsync(n => n.TaskId == task.Id && n.Attempt == task.Attempt
                && n.SourceEventId == failed.Id && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate, ct)) return;
        var session = task.AgentSessionId is { } sid ? await db.AgentSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sid, ct) : null;
        var parent = task.ParentSessionId is { } pid ? await db.AgentSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == pid, ct) : null;
        var parentGeneration = historical ? await db.TaskSupervisionNotices.AsNoTracking()
            .Where(n => n.TaskId == task.Id && n.Attempt == task.Attempt
                && n.Purpose == TaskSupervisionNoticePurpose.CeilingParent && n.ParentSessionId == task.ParentSessionId)
            .OrderByDescending(n => n.CreatedAt).Select(n => n.ParentStartedAt).FirstOrDefaultAsync(ct)
            : parent?.StartedAt;
        // New saves follow their existing generation-fenced writer. Old rows must carry an
        // original accepted coordinate; the current session row alone cannot reconstruct one.
        DateTime? generation = historical
            ? task.CeilingHoldAttempt == task.Attempt && task.CeilingHoldSessionId == task.AgentSessionId
                && task.CeilingHoldDispatchedAt == task.DispatchedAt ? task.CeilingHoldSessionStartedAt : null
            : db.RuntimeActionTaskFence?.Generation ?? (session is { } known && known.StartedAt != default ? known.StartedAt : null);
        var frontier = session is null ? 0 : await db.TranscriptEntries
            .Where(t => t.AgentSessionId == session.Id && (!historical || t.Timestamp != null && t.Timestamp <= failed.At))
            .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;
        var id = Guid.NewGuid();
        var why = task.FailureReason ?? failed.Detail;
        if (why.Length > 600) why = why[..600] + "…";
        var body = $"[task-supervision:{id:D}] {DelegationReportFormatter.TaskMarker(task.Id)}\n"
            + $"task={task.Id:D}; attempt={task.Attempt}; session={task.AgentSessionId}; generation={generation:O}; failure-event={failed.Id:D}\n"
            + $"Why Failed: {why}\nYour task is Failed. Stop starting further work, preserve/push your authorized work, "
            + "and return a complete report. That report is evidence for triage; it will not automatically change "
            + "this Failed outcome or launch a next stage.";
        var noRecipient = task.AgentSessionId is null;
        var provedExit = session is { EndedAt: not null } && session.Status is SessionStatus.Stopped or SessionStatus.Failed;
        db.TaskSupervisionNotices.Add(new TaskSupervisionNotice
        {
            Id = id, TaskId = task.Id, Attempt = task.Attempt, SourceEventId = failed.Id,
            Purpose = TaskSupervisionNoticePurpose.FailureDelegate, SessionId = task.AgentSessionId,
            SessionStartedAt = generation, DispatchedAt = task.DispatchedAt,
            ParentSessionId = task.ParentSessionId, ParentStartedAt = parentGeneration,
            RecipientSessionId = task.AgentSessionId, RecipientStartedAt = generation,
            RecipientFloor = frontier, FailureFrontier = frontier,
            ReportFloor = task.RepliedAtSequence ?? 0, ScanFrontier = frontier,
            Body = body, Digest = DelegationNoteDigest.Compute(body), CreatedAt = now,
            State = noRecipient || provedExit || generation is null ? TaskSupervisionNoticeState.NotApplicable : TaskSupervisionNoticeState.Pending,
            ReasonCode = generation is null ? "legacy_identity_unknown" : noRecipient ? "no_accepted_session"
                : provedExit ? "session_exit_recorded" : null,
            ReportWatchClosedAt = generation is null ? now : null,
            ReportWatchCloseReason = generation is null ? "legacy_identity_unknown" : null,
            NextReportScanAt = generation is null ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : now,
            NextAttemptAt = generation is null ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : now,
        });
    }

    internal static Task RecordFailureAsync(AppDbContext db, AgentTask task,
        AgentTaskEvent failed, DateTime now, CancellationToken ct) =>
        RecordFailureAsync(db, task, failed, now, false, ct);
}

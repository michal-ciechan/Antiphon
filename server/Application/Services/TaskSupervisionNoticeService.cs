using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Bounded durable work for the existing completion pump. Queue state is never receipt.</summary>
internal sealed class TaskSupervisionNoticeService(AppDbContext db, SessionMessageQueueService queue, TimeProvider clock)
{
    internal static IQueryable<TaskSupervisionNotice> Unresolved(AppDbContext store) => store.TaskSupervisionNotices
        .Where(n => n.State == TaskSupervisionNoticeState.Pending
            || n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent && n.State == TaskSupervisionNoticeState.Superseded
            || n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate && n.ReportForwardedAt == null
                && n.SessionStartedAt != null && n.State != TaskSupervisionNoticeState.Superseded
                && n.ReasonCode != "report_attribution_superseded");

    internal async Task RecoverAsync(Guid? taskId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = db.TaskSupervisionNotices.Where(n => n.NextAttemptAt <= now
            && (n.State == TaskSupervisionNoticeState.Pending
                || n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate && n.ReportForwardedAt == null));
        if (taskId is { } id) due = due.Where(n => n.TaskId == id);
        var rows = await due.OrderBy(n => n.NextAttemptAt).ThenBy(n => n.Id).Take(32).ToListAsync(ct);
        if (rows.Count < 32)
        {
            var missing = db.AgentTasks.AsNoTracking().Where(t => t.Status == AgentTaskStatus.Failed
                && !db.TaskSupervisionNotices.Any(n => n.TaskId == t.Id && n.Attempt == t.Attempt
                    && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate)
                && db.AgentTaskEvents.Any(e => e.AgentTaskId == t.Id && e.Type == AgentTaskEventType.Failed));
            if (taskId is { } one) missing = missing.Where(t => t.Id == one);
            foreach (var task in await missing.OrderBy(t => t.Id).Take(32 - rows.Count).ToListAsync(ct))
            {
                var failed = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == task.Id
                    && e.Type == AgentTaskEventType.Failed).OrderByDescending(e => e.At).FirstAsync(ct);
                await TaskSupervisionNoticeWriter.RecordFailureAsync(db, task, failed, now, historical: true, ct);
            }
            await db.SaveChangesAsync(ct);
            rows = rows.Concat(db.TaskSupervisionNotices.Local.Where(n => n.CreatedAt == now)).DistinctBy(n => n.Id).Take(32).ToList();
        }
        // Failure notice delivery does not close report discovery, including ended generations.
        foreach (var failure in rows.Where(n => n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate))
        {
            var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == failure.TaskId, ct);
            if (task is null || task.Attempt != failure.Attempt || task.AgentSessionId != failure.SessionId
                || task.DispatchedAt != failure.DispatchedAt || task.ParentSessionId != failure.ParentSessionId)
            {
                failure.ReasonCode = "report_attribution_superseded";
                failure.NextAttemptAt = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
                if (failure.State == TaskSupervisionNoticeState.Pending) failure.State = TaskSupervisionNoticeState.Superseded;
                continue;
            }
            if (task is not null) await LateFailedReportForwarder.DiscoverAsync(db, task, failure, now, ct);
            failure.NextAttemptAt = now.AddMinutes(1);
        }
        var deliveries = rows.Concat(db.TaskSupervisionNotices.Local.Where(n =>
                n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent && n.CreatedAt == now))
            .DistinctBy(n => n.Id).OrderBy(n => n.Purpose == TaskSupervisionNoticePurpose.LateFailedParent ? 0 : 1).Take(32).ToList();
        foreach (var notice in deliveries)
        {
            if (notice.State != TaskSupervisionNoticeState.Pending) continue;
            if (await ConfirmAsync(notice, ct)) continue;
            if (!await CanSendAsync(db, notice, ct)) continue;
            try
            {
                await queue.EnqueueSupervisionNoticeAsync(notice, message => notice.QueueMessageId = message, ct);
                await db.SaveChangesAsync(ct);
                await ConfirmAsync(notice, ct);
                notice.NextAttemptAt = now.AddMinutes(1);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // The same purpose/queue key is retried after backoff, including post-insert faults.
                notice.ReasonCode = "queue_enqueue_failed";
                notice.NextAttemptAt = now.AddMinutes(1);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> ConfirmAsync(TaskSupervisionNotice notice, CancellationToken ct)
    {
        var row = await db.SessionQueuedMessages.AsNoTracking()
            .SingleOrDefaultAsync(m => m.SourceTaskSupervisionNoticeId == notice.Id, ct);
        if (row is null) return false;
        notice.QueueMessageId = row.Id;
        if (row.DeliveryAttempts == 0 || row.LastDeliveryStartedAt is null
            || row.LastDeliveryGeneration is not { } attempted || notice.RecipientStartedAt is not { } accepted
            || !SessionGeneration.Equal(attempted, accepted)) return false;
        var floor = Math.Max(notice.RecipientFloor, row.LastDeliveryBaselineSequence ?? 0);
        var prompts = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == notice.RecipientSessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor
                && t.CreatedAt >= row.LastDeliveryStartedAt)
            .OrderBy(t => t.Sequence).Take(256).ToListAsync(ct);
        var receipt = prompts.FirstOrDefault(t => PromptSubmissionMatch.IsConfirmedBy(notice.Body, t.Text ?? ""));
        if (receipt is null) return false;
        notice.State = TaskSupervisionNoticeState.Told;
        notice.ToldAt = receipt.Timestamp ?? receipt.CreatedAt;
        notice.ConfirmingPromptSequence = receipt.Sequence;
        notice.ReasonCode = null;
        return true;
    }

    internal static async Task<bool> SourceCurrentAsync(AppDbContext db, AgentTask task,
        TaskSupervisionNotice notice, CancellationToken ct)
    {
        if (task.Attempt != notice.Attempt || task.DispatchedAt != notice.DispatchedAt
            || task.AgentSessionId != notice.SessionId || task.ParentSessionId != notice.ParentSessionId
            || notice.SessionStartedAt is not { } generation || generation == default || notice.SessionId is not { } sid
            || task.Status != AgentTaskStatus.Failed && notice.Purpose != TaskSupervisionNoticePurpose.CeilingParent)
            return false;
        if (!await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == sid && s.StartedAt == generation, ct)) return false;
        return !await db.AgentTasks.AsNoTracking().AnyAsync(t => t.Id != task.Id && t.CreatedAt > task.CreatedAt
            && (t.AgentSessionId == sid || task.AgentId != null && t.AgentId == task.AgentId), ct);
    }

    internal static async Task<bool> CanSendAsync(AppDbContext db, TaskSupervisionNotice notice, CancellationToken ct)
    {
        if (notice.State != TaskSupervisionNoticeState.Pending) return false;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == notice.TaskId, ct);
        if (task is null || task.Attempt != notice.Attempt || task.DispatchedAt != notice.DispatchedAt
            || task.AgentSessionId != notice.SessionId || task.ParentSessionId != notice.ParentSessionId
            || notice.Purpose == TaskSupervisionNoticePurpose.CeilingParent
                && (task.Status != AgentTaskStatus.Working || !CeilingHoldFacts.Active(task)
                    || task.CeilingHoldEpisodeId != notice.EpisodeId)
            || notice.Purpose != TaskSupervisionNoticePurpose.CeilingParent && task.Status != AgentTaskStatus.Failed)
        {
            notice.State = TaskSupervisionNoticeState.Superseded;
            notice.ReasonCode = "task_attempt_or_episode_changed";
            return false;
        }
        if (notice.SessionStartedAt is not { } sourceGeneration || sourceGeneration == default
            || notice.RecipientStartedAt is null || notice.RecipientSessionId is null)
        {
            notice.ReasonCode = "accepted_identity_unavailable";
            return false;
        }
        if (!await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == notice.SessionId, ct))
        {
            notice.ReasonCode = "source_session_unavailable";
            return false;
        }
        if (!await SourceCurrentAsync(db, task, notice, ct))
        {
            notice.State = TaskSupervisionNoticeState.Superseded;
            notice.ReasonCode = "source_generation_or_owner_changed";
            return false;
        }
        var recipient = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == notice.RecipientSessionId, ct);
        if (recipient is null) { notice.ReasonCode = "recipient_unavailable"; return false; }
        if (recipient.StartedAt != notice.RecipientStartedAt)
        {
            notice.State = TaskSupervisionNoticeState.Superseded;
            notice.ReasonCode = "recipient_generation_changed";
            return false;
        }
        if (recipient.Status != SessionStatus.Running || recipient.EndedAt is not null)
        {
            // A missing parent remains unresolved; it is never silently redirected.
            notice.ReasonCode = "recipient_not_running";
            if (notice.Purpose == TaskSupervisionNoticePurpose.FailureDelegate && recipient.EndedAt is not null)
                notice.State = TaskSupervisionNoticeState.NotApplicable;
            return false;
        }
        return true;
    }
}

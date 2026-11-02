using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1488 and CARD-1497: what a task that has just become terminal (Canceled, or Failed by the
/// dispatcher, which includes the dead-session process-exit path) must not leave behind. Its lease
/// waiters on every repository are dropped, so no land yields to a sync that is never re-handed.
/// Its never-typed closing-line nudge and the parent's never-typed <c>task-wait:&lt;id&gt;</c> note
/// are retired with the CARD-1474 row rules: only rows bound to this task's recorded identity, only
/// while still Pending with no delivery attempt, through the queue's under-lock re-check.
///
/// <para>Keyed by the task id, never by repository or session, so another task's waiter and notes
/// are untouched. Runs after the terminal commit and outside any queue lock the caller holds.
/// Idempotent and best-effort: a task with nothing to retire is a no-op, and a failure here is
/// logged and never undoes the terminal status.</para>
/// </summary>
public static class TerminalTaskResidue
{
    public static async Task RetireAsync(
        AgentTask task, AppDbContext db, SessionMessageQueueService? queue, RepositoryLeaseWaiters? waiters,
        ILogger logger, CancellationToken ct)
    {
        waiters?.Clear(task.Id);
        if (queue is null)
            return;
        var shortId = DelegationReportFormatter.Short(task.Id);
        try
        {
            if (task.ReportNudgeMessageId is Guid nudgeId && task.AgentSessionId is Guid sessionId
                && await queue.CancelPendingIfUntypedAsync(sessionId, nudgeId, ct))
            {
                logger.LogInformation(
                    "Task {ShortId}: {Status} retired the never-typed closing-line nudge {MessageId}",
                    shortId, task.Status, nudgeId);
            }

            if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
                return;
            var waitKey = $"task-wait:{task.Id:N}";
            var waitNotes = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == parentSession
                    && m.ConversationKey == waitKey
                    && m.SourceTaskId == task.Id
                    && m.Status == QueuedMessageStatus.Pending
                    && m.DeliveryAttempts == 0)
                .Select(m => m.Id)
                .ToListAsync(ct);
            foreach (var noteId in waitNotes)
            {
                if (await queue.CancelPendingIfUntypedAsync(parentSession, noteId, ct))
                {
                    logger.LogInformation(
                        "Task {ShortId}: {Status} retired the parent's waiting note {MessageId}",
                        shortId, task.Status, noteId);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Task {ShortId}: could not retire the closing-line nudge or waiting note", shortId);
        }
    }
}

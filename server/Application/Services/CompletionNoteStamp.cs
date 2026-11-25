using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Caller completion-note idempotency stamp. Sent queue rows age out; sourced tasks do not.
/// Enqueue writes the queue row and this stamp in one transaction. Repair covers a leftover
/// unstamped row (crash between the old two writes) before retention can delete the row and
/// let the completion scanner recreate the note.
/// </summary>
internal static class CompletionNoteStamp
{
    public static bool IsCallerCompletionNote(
        QueuedMessageOrigin origin,
        Guid? sourceLandNotificationId,
        Guid? sourceTaskId,
        string? conversationKey) =>
        origin == QueuedMessageOrigin.Delegation
        && sourceLandNotificationId is null
        && sourceTaskId is not null
        && conversationKey is not null
        && conversationKey.StartsWith("task:", StringComparison.Ordinal);

    /// <summary>
    /// CARD-1503 note identity for a question that Blocks with a report the caller already holds:
    /// the report plus that settlement's Blocked event, so the note is not dropped as a duplicate.
    /// </summary>
    public static string RetainedQuestionDigest(string report, Guid settlementEventId) =>
        DelegationNoteDigest.Compute($"{report}\n\n[retained-question-block {settlementEventId:D}]");

    // Called only after the recipient's complete submitted prompt has been matched.
    // A delayed queue attempt for an earlier settlement must not acknowledge a later one.
    public static async Task RecordReceiptsAsync(AppDbContext db, IEnumerable<SessionQueuedMessage> messages,
        CancellationToken ct)
    {
        foreach (var message in messages)
        {
            if (!IsCallerCompletionNote(message.Origin, message.SourceLandNotificationId, message.SourceTaskId,
                    message.ConversationKey) || message.ContentDigest is null) continue;
            var task = await db.AgentTasks.AsNoTracking()
                .Where(t => t.Id == message.SourceTaskId && t.ParentSessionId == message.AgentSessionId
                    && t.CompletedAt != null && t.CompletedAt <= message.CreatedAt)
                .Select(t => new { t.Result, t.CompletedAt }).SingleOrDefaultAsync(ct);
            if (task?.Result is null) continue;
            if (DelegationNoteDigest.Compute(task.Result) != message.ContentDigest)
            {
                // A retained-question note is identified by the report and its own Blocked
                // settlement event. Only the event of the settlement being closed can match, so a
                // later settlement with identical text still needs its own note or read.
                var settlement = await db.AgentTaskEvents.AsNoTracking()
                    .Where(e => e.AgentTaskId == message.SourceTaskId && e.At == task.CompletedAt
                        && e.Type == AgentTaskEventType.Blocked)
                    .Select(e => e.Id).ToListAsync(ct);
                if (!settlement.Any(id => RetainedQuestionDigest(task.Result, id) == message.ContentDigest)) continue;
            }

            var prompts = db.TranscriptEntries.AsNoTracking().Where(e =>
                e.AgentSessionId == message.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt && e.Text != null);
            if (message.LastDeliveryBaselineSequence is long floor)
                prompts = prompts.Where(e => e.Sequence > floor);
            else if (message.LastDeliveryStartedAt is DateTime started)
                prompts = prompts.Where(e => e.Timestamp >= started);
            else continue;
            long? sequence = null;
            await foreach (var prompt in prompts.OrderBy(e => e.Sequence).Select(e => new { e.Sequence, e.Text })
                .AsAsyncEnumerable().WithCancellation(ct))
            {
                if (PromptSubmissionMatch.RequiresTextMatch(message.Body)
                    ? PromptSubmissionMatch.IsCompleteIn(message.Body, prompt.Text)
                    : !string.IsNullOrWhiteSpace(message.Body) && prompt.Text!.Contains(message.Body, StringComparison.Ordinal))
                {
                    sequence = prompt.Sequence;
                    break;
                }
            }
            if (sequence is null) continue;

            await db.AgentTasks.Where(t => t.Id == message.SourceTaskId && t.ParentSessionId == message.AgentSessionId
                    && t.CompletedAt == task.CompletedAt && t.Result == task.Result)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.ReceivedReportCompletedAt, task.CompletedAt)
                    .SetProperty(t => t.ReceivedReportSessionId, message.AgentSessionId)
                    .SetProperty(t => t.ReceivedReportPromptSequence, sequence), ct);
        }
    }

    // A pre-upgrade Sent row does not say whether its verdict came from the screen.
    // Recover only a complete submitted prompt while both its queue row and history exist.
    public static async Task RecoverReceiptsAsync(AppDbContext db, IQueryable<SessionQueuedMessage> rows, CancellationToken ct)
    {
        var eligible = rows.AsNoTracking().Where(m => m.SourceTaskId != null && m.SourceLandNotificationId == null
            && m.Origin == QueuedMessageOrigin.Delegation && m.Status == QueuedMessageStatus.Sent
            && m.ContentDigest != null && m.ConversationKey != null && m.ConversationKey.StartsWith("task:")
            && (m.LastDeliveryBaselineSequence != null || m.LastDeliveryStartedAt != null)
            && db.AgentTasks.Any(t => t.Id == m.SourceTaskId && t.ParentSessionId == m.AgentSessionId
                && t.CompletedAt <= m.CreatedAt && t.Result != null
                && (t.ReceivedReportCompletedAt != t.CompletedAt || t.ReceivedReportSessionId != t.ParentSessionId)));
        Guid? cursor = null;
        while (true)
        {
            var page = await (cursor is Guid after ? eligible.Where(m => m.Id.CompareTo(after) > 0) : eligible)
                .OrderBy(m => m.Id).Take(64).ToListAsync(ct);
            await RecordReceiptsAsync(db, page, ct);
            if (page.Count < 64) return;
            cursor = page[^1].Id;
        }
    }

    public static Task ApplyAsync(
        AppDbContext db, Guid taskId, string? digest, DateTime queuedAt, CancellationToken ct) =>
        db.AgentTasks.Where(t => t.Id == taskId && (t.CompletionNoteQueuedAt == null
                || t.CompletionNoteQueuedAt < t.CompletedAt && queuedAt >= t.CompletedAt))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.CompletionNoteQueuedAt, queuedAt)
                .SetProperty(t => t.CompletionNoteDigest, digest), ct);

    public static async Task RepairFromAsync(
        AppDbContext db, IQueryable<SessionQueuedMessage> rows, CancellationToken ct)
    {
        var unstamped = rows.AsNoTracking()
            .Where(m => m.Origin == QueuedMessageOrigin.Delegation
                && m.SourceLandNotificationId == null
                && m.SourceTaskId != null
                && m.ConversationKey != null
                && m.ConversationKey.StartsWith("task:")
                && db.AgentTasks.Any(t => t.Id == m.SourceTaskId && (t.CompletionNoteQueuedAt == null
                    || t.CompletionNoteQueuedAt < t.CompletedAt && m.CreatedAt >= t.CompletedAt)));

        while (true)
        {
            var pending = await unstamped.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(128)
                .Select(m => new { TaskId = m.SourceTaskId!.Value, m.ContentDigest, m.CreatedAt })
                .ToListAsync(ct);
            foreach (var row in pending.GroupBy(r => r.TaskId).Select(g => g.First()))
                await ApplyAsync(db, row.TaskId, row.ContentDigest, row.CreatedAt, ct);
            if (pending.Count < 128) return;
        }
    }
}

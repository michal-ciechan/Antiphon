using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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

    public static Task ApplyAsync(
        AppDbContext db, Guid taskId, string? digest, DateTime queuedAt, CancellationToken ct) =>
        db.AgentTasks.Where(t => t.Id == taskId && t.CompletionNoteQueuedAt == null)
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
                && db.AgentTasks.Any(t => t.Id == m.SourceTaskId && t.CompletionNoteQueuedAt == null));

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

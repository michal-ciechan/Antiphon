using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0527 F3. Rebuild a missing caller-note queue row from the settlement obligation.
/// SourceLanding tasks without a stored body keep the Result rebuild path.
/// </summary>
internal static class CompletionNoteRecovery
{
    public static async Task RecoverMissingAsync(
        IServiceProvider services, AppDbContext db, CancellationToken ct)
    {
        var owed = await db.AgentTasks.AsNoTracking()
            .Where(t => t.ParentSessionId != null
                && t.ReplyTo == AgentTaskReplyTo.Session
                && t.Result != null
                && t.CompletionNoteQueuedAt == null
                && (t.Status == AgentTaskStatus.Succeeded
                    || t.Status == AgentTaskStatus.Failed
                    || t.Status == AgentTaskStatus.Canceled
                    || t.Status == AgentTaskStatus.Blocked)
                && (t.CompletionNoteBody != null || t.SourceLandingOperationId != null))
            .Select(t => new
            {
                t.Id,
                Parent = t.ParentSessionId!.Value,
                t.RootTaskId,
            })
            .ToListAsync(ct);
        if (owed.Count == 0)
            return;

        var owedIds = owed.Select(r => r.Id).ToList();
        await CompletionNoteStamp.RepairFromAsync(
            db,
            db.SessionQueuedMessages.Where(m => m.SourceTaskId != null && owedIds.Contains(m.SourceTaskId.Value)),
            ct);

        var queue = services.GetRequiredService<SessionMessageQueueService>();
        var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        var flushes = services.GetService<CompletionNoteFlushQueue>();
        foreach (var row in owed)
        {
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.Id, ct);
            if (task.CompletionNoteQueuedAt is not null)
                continue;
            if (await db.SessionQueuedMessages.AsNoTracking().AnyAsync(
                    m => m.SourceTaskId == row.Id
                        && m.SourceLandNotificationId == null
                        && m.Origin == QueuedMessageOrigin.Delegation
                        && m.Status != QueuedMessageStatus.Canceled, ct))
                continue;
            if (services.GetService<LandDeliveryBoundary>() is { } boundary)
                await boundary.ReachedAsync("completion-scan", task.Id, row.Parent, ct);
            var report = task.Result ?? "";
            string body;
            string? header;
            if (!string.IsNullOrEmpty(task.CompletionNoteBody))
            {
                body = task.CompletionNoteBody;
                header = task.CompletionNoteHeader;
            }
            else
            {
                var note = DelegationReportFormatter.BuildCompletionNote(
                    task, settings, report, land: await LandCompletionFacts.LoadAsync(db, task, ct));
                body = note.Body;
                header = note.Header;
            }

            await queue.EnqueueAsync(
                row.Parent, body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                task.Id, DelegationNoteDigest.Compute(report), header, deliverIfIdle: false);
            flushes?.TryEnqueue(row.Parent);
        }
    }
}

using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    /// <summary>One durable publication per logical Check, committed before any terminal write.</summary>
    public async Task PublishCheckRequestAsync(Guid requestId, Guid callerSessionId, string body,
        string detail, bool suppress, CancellationToken ct)
    {
        var gate = GetLock(callerSessionId);
        await gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var request = await db.SpecialistRequests.FromSqlInterpolated(
                $"SELECT * FROM \"SpecialistRequests\" WHERE \"Id\" = {requestId} FOR UPDATE").SingleAsync(ct);
            if (request.Purpose != SpecialistRequestPurpose.Check || request.CompletedAt is null || request.HealthAppliedAt is null)
                throw new ConflictException("Check publication requires committed request and health evidence.", "specialist_publication_not_ready");
            if (request.CallerPublishedAt is null)
            {
                var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.CheckedTaskId, ct);
                var current = task is not null && task.ParentSessionId == callerSessionId && task.ReplyTo == AgentTaskReplyTo.Session
                    && Math.Max(1, task.CheckCount) == request.CheckNumber && request.Status != SpecialistRequestStatus.Canceled;
                var now = UtcNow();
                if (!suppress && current)
                {
                    request.CallerMessageId ??= Guid.NewGuid();
                    var sequence = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == callerSessionId)
                        .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = request.CallerMessageId.Value, AgentSessionId = callerSessionId, Body = body.Trim(),
                        Sequence = sequence, Status = QueuedMessageStatus.Pending, CreatedAt = now,
                        Origin = QueuedMessageOrigin.Check, SourceTaskId = request.CheckedTaskId,
                        ConversationKey = AgentTaskCheckService.ConversationKey(request.CheckedTaskId!.Value),
                    });
                }
                if (task is not null && !await db.AgentTaskEvents.AnyAsync(e => e.Id == request.Id, ct))
                    db.AgentTaskEvents.Add(new AgentTaskEvent { Id = request.Id, AgentTaskId = task.Id,
                        Type = AgentTaskEventType.Check, At = now, Detail = detail });
                // This bit and the queue/timeline rows are one commit. A restart observes all or
                // none; it never needs to guess whether the caller was already told.
                request.CallerPublishedAt = now;
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        finally { gate.Release(); }
        // The normal flush owns transcript confirmation and late delivery/supersession rules.
        await FlushSessionAsync(callerSessionId, ct);
        await PublishQueueChangedAsync(await GetQueueAsync(callerSessionId, ct), ct);
    }
}

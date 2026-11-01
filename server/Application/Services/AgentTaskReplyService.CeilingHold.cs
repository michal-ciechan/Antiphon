using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

// Constructed by the API only after OperatorCredential.Require; never bound from a request.
internal sealed record CeilingOperatorAuthorization(string Actor);

public sealed partial class AgentTaskReplyService
{
    internal async Task<T> WithSessionSettlementAsync<T>(Guid sessionId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = _settleLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    internal async Task<AgentTaskSummaryDto> AnswerCeilingAsync(Guid taskId, ReplyToAgentTaskRequest request,
        CeilingOperatorAuthorization? authority, CancellationToken ct)
    {
        if (request.CeilingAction is not { } action || !Enum.IsDefined(action)
            || request.EpisodeId is null || request.EpisodeId == Guid.Empty
            || request.Message is not null || request.Round is not null || request.Origin is not null
            || request.InterruptCurrentTurn || request.RequestId is not null || request.CeilingReason?.Length > 1000)
            throw new ValidationException("ceilingAction", "A ceiling decision requires an exclusive typed action and episode id.");
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
        var task = await db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId);
        if (task.AgentSessionId is not { } sessionId)
            throw StaleCeilingEpisode();
        return await WithSessionSettlementAsync(sessionId, async () =>
        {
            var recipient = queue.GetLock(sessionId);
            await recipient.WaitAsync(ct);
            try
            {
                await db.Entry(task).ReloadAsync(ct);
                if (!CeilingHoldFacts.Active(task) || task.CeilingHoldEpisodeId != request.EpisodeId
                    || task.AgentSessionId != sessionId || task.CeilingHoldSessionStartedAt is not { } generation
                    || !await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId
                        && s.StartedAt == generation && s.Status == SessionStatus.Running && s.EndedAt == null, ct))
                    throw StaleCeilingEpisode();
                var now = UtcNow();
                var previous = task.NextCeilingMinutes;
                var next = CeilingHoldFacts.Rearm(task, action, request.ExtendMinutes, request.OperatorExtend,
                    authority is not null, request.CeilingReason, now);
                task.NextCeilingMinutes = next;
                task.CeilingAcknowledgedAt = now;
                task.CeilingOperatorExtended = request.OperatorExtend;
                task.ConcurrencyToken = Guid.NewGuid();
                db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Warning,
                    "ceiling-decision " + JsonSerializer.Serialize(new { episode = request.EpisodeId,
                        task.Attempt, sessionId, generation, task.DispatchedAt, action = action.ToString(),
                        oldBound = previous, newBound = next, operatorExtend = request.OperatorExtend,
                        actor = authority?.Actor ?? "caller", reason = request.CeilingReason,
                        initialMinutes = task.ResolvedCeilingMinutes, initialSource = task.ResolvedCeilingSource }), now));
                db.RuntimeActionTaskFence = new(task.Id, task.Attempt, sessionId, generation);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { throw StaleCeilingEpisode(); }
                finally { db.RuntimeActionTaskFence = null; }
                // No queue input, workspace admission, report watermark or attempt is changed.
                await PublishAsync(task, ct);
                queue.SupervisionChanged(task.Id);
                var family = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
                return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetSummaryAsync(task, family);
            }
            finally { recipient.Release(); }
        }, ct);
    }

    private static ConflictException StaleCeilingEpisode() =>
        new("The ceiling episode is no longer current. Reload the task before deciding.", "ceiling_episode_stale");
}

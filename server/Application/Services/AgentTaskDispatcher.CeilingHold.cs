using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AgentTaskDispatcher
{
    private async Task<bool> ObserveCeilingSessionAsync(AgentSession session, CancellationToken ct)
    {
        var runner = _runners?.Resolve(session.RunnerId) ?? _runnerClient;
        if (runner is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var observed = await runner.GetAsync(session.Id, budget.Token);
            return observed.SessionId == session.Id && observed.Status == "Running"
                && observed.AcceptedStartedAt is { } accepted && SessionGeneration.Equal(accepted, session.StartedAt);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Ceiling observation of session {SessionId} is unknown; retaining its task", session.Id);
            return false;
        }
    }

    private Task<bool> TryHoldCeilingAsync(AgentTask task, DateTime generation, CancellationToken ct) =>
        _replies is null ? HoldCeilingLockedAsync(task, generation, ct)
            : _replies.WithSessionSettlementAsync(task.AgentSessionId!.Value,
                () => HoldCeilingLockedAsync(task, generation, ct), ct);

    private async Task<bool> HoldCeilingLockedAsync(AgentTask task, DateTime generation, CancellationToken ct)
    {
        var attempt = task.Attempt;
        var dispatch = task.DispatchedAt;
        var sessionId = task.AgentSessionId!.Value;
        var gate = _queue.GetLock(sessionId);
        await gate.WaitAsync(ct);
        try
        {
            await _db.Entry(task).ReloadAsync(ct);
            if (task.Attempt != attempt || task.DispatchedAt != dispatch || task.AgentSessionId != sessionId
                || task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working)) return false;
            var session = await _db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            // An uncertain observation cannot establish either a new hold or an idle failure.
            if (session is null || generation == default || session.StartedAt != generation
                || session.Status != SessionStatus.Running || session.EndedAt is not null
                || _runtime?.RemoteInventoryPending(session.RunnerId) == true) return false;
            var verdict = await TaskDeadlinePolicy.EvaluateAsync(_db, task, UtcNow(), _settings, ct);
            if (verdict is not { Breached: true, Kind: TaskDeadlinePolicy.DeadlineKind.Ceiling, Boot: null, Working: true })
                return false;
            if (!CeilingHoldFacts.CanHold(task.Status, generation, session.StartedAt,
                session.Status == SessionStatus.Running, _runtime?.RemoteInventoryPending(session.RunnerId) != true,
                verdict.Working, verdict.Boot is not null, verdict.PhaseDeadline?.Breached == true)) return false;
            if (CeilingHoldFacts.Active(task)) return true;
            if (task.ResolvedCeilingMinutes is null) CeilingHoldFacts.Resolve(task, _settings);
            var now = UtcNow();
            task.Status = AgentTaskStatus.Working;
            task.CeilingHoldEpisodeId = Guid.NewGuid();
            task.CeilingHeldAt = now;
            task.CeilingHoldAttempt = attempt;
            task.CeilingHoldSessionId = sessionId;
            task.CeilingHoldSessionStartedAt = generation;
            task.CeilingHoldDispatchedAt = dispatch;
            task.CeilingAcknowledgedAt = null;
            task.ConcurrencyToken = Guid.NewGuid();
            var held = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = task.Id,
                Type = AgentTaskEventType.Held, At = now,
                Detail = $"ceiling-hold episode={task.CeilingHoldEpisodeId:D}; attempt={attempt}; session={sessionId:D}; generation={generation:O}. "
                    + "The delegate continues, stays Working and counted. next=decide" };
            _db.AgentTaskEvents.Add(held);
            var facts = await CeilingHoldFacts.DescribeAsync(_db, task, now, _settings, ct);
            var parent = task.ParentSessionId is { } parentId ? await _db.AgentSessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == parentId, ct) : null;
            var id = Guid.NewGuid();
            var body = $"[task {DelegationReportFormatter.Short(task.Id)} held] task={task.Id:D} attempt={attempt} "
                + $"episode={task.CeilingHoldEpisodeId:D} session={sessionId:D} generation={generation:O}\n{facts!.Question}";
            _db.TaskSupervisionNotices.Add(new TaskSupervisionNotice { Id = id, TaskId = task.Id, Attempt = attempt,
                SourceEventId = held.Id, Purpose = TaskSupervisionNoticePurpose.CeilingParent,
                EpisodeId = task.CeilingHoldEpisodeId, SessionId = sessionId, SessionStartedAt = generation,
                DispatchedAt = dispatch, ParentSessionId = task.ParentSessionId, ParentStartedAt = parent?.StartedAt,
                RecipientSessionId = task.ReplyTo == AgentTaskReplyTo.Session ? task.ParentSessionId : null,
                RecipientStartedAt = parent?.StartedAt,
                RecipientFloor = parent is null ? 0 : await _db.TranscriptEntries.Where(t => t.AgentSessionId == parent.Id)
                    .MaxAsync(t => (long?)t.Sequence, ct) ?? 0,
                Body = body, Digest = DelegationNoteDigest.Compute(body), CreatedAt = now, NextAttemptAt = now,
                State = task.ReplyTo == AgentTaskReplyTo.Session && task.ParentSessionId is not null
                    ? TaskSupervisionNoticeState.Pending : TaskSupervisionNoticeState.NotApplicable,
                ReasonCode = task.ParentSessionId is null ? "no_parent" : null });
            var previousFence = _db.RuntimeActionTaskFence;
            _db.RuntimeActionTaskFence = new(task.Id, attempt, sessionId, generation);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { _db.ChangeTracker.Clear(); return false; }
            finally { _db.RuntimeActionTaskFence = previousFence; }
            await _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
            _queue.SupervisionChanged(task.Id);
            return true;
        }
        finally { gate.Release(); }
    }
}

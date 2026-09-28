using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class AgentTaskDecisionQuestionService(
    AppDbContext db, IEventBus? eventBus = null, TimeProvider? timeProvider = null)
{
    public async Task<InternalDecisionQuestionResponse> CheckAsync(Guid taskId,
        InternalDecisionQuestionRequest request, AgentTaskService.Caller caller, CancellationToken ct)
    {
        // This is a worker-self operation, not the usual permission to delegate another task.
        if (caller.CapabilityId is not null || (caller.Task is null && caller.SessionId is null)
            || (caller.Task is not null && caller.Task.Id != taskId))
            throw new ForbiddenException("Only this task's worker may ask an internal decision question.",
                "decision_question_self_only");

        var normalized = InternalDecisionQuestionPolicy.Normalize(request);
        var canonical = InternalDecisionQuestionPolicy.CanonicalJson(normalized);
        var payloadHash = InternalDecisionPolicy.Hash(canonical);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Task writers take PostgreSQL's ROW EXCLUSIVE table lock. Acquire the compatible SHARE
        // lock before the row lock so another task cannot become bound to this session between
        // the binding count and our commit, including a status-only update of an existing row.
        // A writer that committed first is rechecked below; a later writer waits for this check.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"AgentTasks\" IN SHARE MODE", ct);
        var task = await db.AgentTasks.FromSqlInterpolated(
                $"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new ConflictException("The task no longer exists.", "decision_question_stale");

        if (caller.Task is null && caller.SessionId != task.AgentSessionId)
        {
            // A session credential from another task is an identity failure. A previously
            // recorded binding of this session to this task is a stale attempt instead.
            var previouslyBound = await db.AgentTaskDecisionQuestions.AsNoTracking().AnyAsync(q =>
                q.AgentTaskId == taskId && q.AgentSessionId == caller.SessionId, ct);
            if (previouslyBound)
                throw new ConflictException("The worker session changed.", "decision_question_stale");
            throw new ForbiddenException("This session is not this task's worker.",
                "decision_question_self_only");
        }
        if (caller.Task is not null && caller.SessionId != task.AgentSessionId)
            throw new ConflictException("The worker session changed.", "decision_question_stale");

        if (task.Attempt != normalized.Attempt
            || task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            || task.AgentSessionId is not Guid sessionId)
            throw new ConflictException("This question is not bound to the current active attempt.",
                "decision_question_stale");

        var live = await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
        if (!live)
            throw new ConflictException("The worker session is not live.", "decision_question_stale");
        var bindings = await db.AgentTasks.AsNoTracking().CountAsync(t =>
            t.AgentSessionId == sessionId
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working
                || t.Status == AgentTaskStatus.Blocked), ct);
        if (bindings != 1)
            throw new ConflictException("The worker session has an ambiguous task binding.",
                "decision_question_stale");

        var prior = await db.AgentTaskDecisionQuestions.AsNoTracking().SingleOrDefaultAsync(q =>
            q.AgentTaskId == taskId && q.Attempt == task.Attempt && q.RequestId == normalized.RequestId, ct);
        if (prior is not null)
        {
            if (prior.CanonicalPayloadJson != canonical)
                throw new ConflictException("This request id was already used for another question.",
                    "decision_question_changed");
            await transaction.CommitAsync(ct);
            return new InternalDecisionQuestionResponse(prior.Id, prior.Disposition,
                prior.Reason, prior.GrantId, prior.Answer);
        }

        var policy = task.InternalDecisionPolicyJson is null
            ? null : InternalDecisionPolicy.ReadStored(task.InternalDecisionPolicyJson);
        var root = task.WorktreePath ?? task.RepoPath ?? task.WorkingDirectory;
        var result = InternalDecisionQuestionPolicy.Evaluate(policy, normalized, root);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var question = new AgentTaskDecisionQuestion
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId, Attempt = task.Attempt,
            AgentSessionId = sessionId, RequestId = normalized.RequestId,
            CanonicalPayloadJson = canonical, PayloadHash = payloadHash,
            PolicyVersion = policy?.Version, PolicyHash = task.InternalDecisionPolicyHash,
            GrantId = result.GrantId, Disposition = result.Disposition,
            Reason = result.Reason, Answer = result.Answer, CreatedAt = now,
        };
        db.AgentTaskDecisionQuestions.Add(question);
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = taskId,
            Type = AgentTaskEventType.DecisionQuestion,
            Detail = $"Question {question.Id:D}: {result.Disposition} ({result.Reason}; grant={result.GrantId ?? "none"})",
            At = now,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (eventBus is not null)
            await eventBus.PublishToAllAsync("AgentTaskChanged",
                new { taskId, rootId = task.RootTaskId }, ct);
        return result with { QuestionId = question.Id };
    }
}

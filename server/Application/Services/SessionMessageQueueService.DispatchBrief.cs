using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    internal sealed record DispatchBriefEnsureResult(DispatchBriefKind Kind, Guid? MessageId, bool Inserted);

    /// <summary>
    /// Runs after the queue gate and the database transaction begin, and before the
    /// task row lock. Tests commit an attempt change that the lock must observe.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeDispatchBriefRowLock { get; set; }

    internal async Task<DispatchBriefEnsureResult> EnsureDispatchBriefAsync(
        DispatchBriefEnsureRequest request, CancellationToken ct)
    {
        var gate = GetLock(request.SessionId);
        await gate.WaitAsync(ct);
        try
        {
            return await EnsureDispatchBriefUnderLockAsync(request, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DispatchBriefEnsureResult> EnsureDispatchBriefUnderLockAsync(
        DispatchBriefEnsureRequest request, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        AgentTask? task = null;
        var committed = false;
        AgentSession? probe = null;
        RemoteSpillCourier.StagedSpill? staged = null;
        DispatchBriefEnsureResult result;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        try
        {
            if (BeforeDispatchBriefRowLock is { } beforeLock)
                await beforeLock(ct);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "AgentTasks" WHERE "Id" = {request.TaskId} FOR UPDATE""", ct);
            task = await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == request.TaskId, ct);
            if (task is null)
            {
                await tx.CommitAsync(ct);
                committed = true;
                return new DispatchBriefEnsureResult(DispatchBriefKind.Uncertain, null, false);
            }

            var session = await db.AgentSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);
            var marker = DelegationReportFormatter.TaskMarker(task.Id);
            var correlation = "task-input:" + task.Id.ToString("D") + ":";
            var related = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.ExecutionTaskId == task.Id
                    || m.SourceTaskId == task.Id
                    || (m.ConversationKey != null && m.ConversationKey.StartsWith(correlation))
                    || m.Body.Contains(marker)
                    || (m.RemoteSpillBody != null && m.RemoteSpillBody.Contains(marker)))
                .ToListAsync(ct);
            var relatedIds = related.Select(m => m.Id).ToArray();
            if (relatedIds.Length > 0)
            {
                var custody = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.RulesCoveredByMessageId != null
                        && relatedIds.Contains(m.RulesCoveredByMessageId.Value))
                    .ToListAsync(ct);
                foreach (var row in custody)
                {
                    if (related.All(existing => existing.Id != row.Id))
                        related.Add(row);
                }
            }

            var prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.Text != null
                    && t.Text.Contains(marker)
                    && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
                .Select(t => new DispatchBriefPromptEvidence(t.AgentSessionId, t.Kind, t.Text, t.Timestamp, t.Sequence))
                .ToListAsync(ct);

            var decision = DispatchBriefEvidence.Classify(
                request,
                new DispatchBriefTaskSnapshot(
                    task.Status,
                    task.Attempt,
                    task.AgentSessionId,
                    task.DispatchedAt,
                    session?.StartedAt,
                    task.Goal),
                related.Select(ToBriefEvidence).ToArray(),
                prompts,
                ReadAbsoluteSpill);

            if (decision.Kind == DispatchBriefKind.Absent)
            {
                if (session is null)
                    result = new DispatchBriefEnsureResult(DispatchBriefKind.Uncertain, null, false);
                else
                {
                    var (inserted, spill) = await InsertAbsentBriefAsync(db, task, session, request, ct);
                    staged = spill;
                    probe = session;
                    result = new DispatchBriefEnsureResult(DispatchBriefKind.Absent, inserted.Id, true);
                }
            }
            else
            {
                if (decision.Hold && task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
                    StageBriefHold(db, task, decision.Reason ?? DispatchBriefEvidence.EvidenceUncertainReason);
                if (db.ChangeTracker.HasChanges())
                    await db.SaveChangesAsync(ct);
                var messageId = decision.Kind is DispatchBriefKind.Reuse or DispatchBriefKind.Received
                    or DispatchBriefKind.AttemptOwned
                    ? related.FirstOrDefault(m => m.AgentSessionId == request.SessionId)?.Id
                    : null;
                result = new DispatchBriefEnsureResult(decision.Kind, messageId, false);
            }

            await tx.CommitAsync(ct);
            committed = true;
        }
        catch
        {
            if (!committed)
            {
                try
                {
                    await tx.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackEx) when (rollbackEx is not OperationCanceledException)
                {
                    _logger.LogDebug(rollbackEx, "Dispatch brief ensure rollback did not complete");
                }

                await DiscardUncommittedBriefAsync(db, task);
                if (staged is not null)
                    _remoteSpills?.Ack(request.SessionId, staged);
            }

            throw;
        }

        // The row and its retained payload are committed. Delivery-state probing comes after,
        // as in EnqueueAsync, so a failed probe cannot roll back the only brief (CARD-1150 F1).
        if (staged is not null)
            _remoteSpills?.Ack(request.SessionId, staged);
        if (probe is not null && _runtime.IsLiveOrUnknown(probe) && !await ReadWorkingAsync(db, probe.Id, ct))
            await DeliverNextLockedAsync(db, request.SessionId, ct);
        return result;
    }

    private async Task<(SessionQueuedMessage Row, RemoteSpillCourier.StagedSpill? Staged)> InsertAbsentBriefAsync(
        AppDbContext db,
        AgentTask task,
        AgentSession session,
        DispatchBriefEnsureRequest request,
        CancellationToken ct)
    {
        var ceilings = AgentTaskDispatcher.CeilingsForBrief(
            _ptyProfile?.Ceilings, session.RunnerCwd, _delegationSettings);
        var fitted = AgentTaskDispatcher.FitBriefForTyping(
            task,
            _delegationSettings,
            ceilings,
            _logger,
            session.AgentKind,
            request.Refocus,
            runnerCwd: session.RunnerCwd,
            stageRemoteSpill: string.IsNullOrWhiteSpace(session.RunnerCwd)
                ? null
                : spill => StageRemoteSpill(session.Id, session.RunnerCwd, spill));
        if (SpecialistInputPolicy.Read(task.SpecialistInputPolicyJson) is not null)
        {
            fitted = await SpillQueueBodyAsync(
                session.Id, fitted, "", null, db, ct,
                specialistInputPolicyJson: task.SpecialistInputPolicyJson);
        }

        var trimmed = (fitted ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(fitted), "Message must not be empty.");

        var now = UtcNow();
        if (now.Kind != DateTimeKind.Utc)
            now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var dispatched = request.DispatchedAt.Kind == DateTimeKind.Utc
            ? request.DispatchedAt
            : DateTime.SpecifyKind(request.DispatchedAt, DateTimeKind.Utc);
        var created = now >= dispatched ? now : dispatched;
        var nextSequence = (await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == session.Id)
            .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = session.Id,
            Body = trimmed,
            Status = QueuedMessageStatus.Pending,
            Sequence = nextSequence,
            CreatedAt = created,
            Origin = QueuedMessageOrigin.Delegation,
            ExecutionDeadlineAt = task.ExecutionDeadlineAt,
            ExecutionTaskId = task.Id,
            SpecialistInputPolicyJson = task.SpecialistInputPolicyJson,
        };
        var staged = BindStagedSpill(session.Id, row);
        db.SessionQueuedMessages.Add(row);
        await db.SaveChangesAsync(ct);
        return (row, staged);
    }

    private void StageBriefHold(AppDbContext db, AgentTask task, string reason)
    {
        if (task.Status == AgentTaskStatus.Blocked
            && string.Equals(task.FailureReason, reason, StringComparison.Ordinal))
            return;
        task.Status = AgentTaskStatus.Blocked;
        task.FailureReason = reason;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Blocked,
            Detail = reason,
            At = UtcNow(),
        });
    }

    private static async Task DiscardUncommittedBriefAsync(AppDbContext db, AgentTask? task)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }

        if (task is null)
            return;
        var tracked = db.Entry(task);
        if (tracked.State == EntityState.Detached)
            return;
        try
        {
            await tracked.ReloadAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            tracked.State = EntityState.Detached;
        }
    }

    private static string? ReadAbsoluteSpill(string path)
    {
        if (!Path.IsPathRooted(path))
            return null;
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DispatchBriefRowEvidence ToBriefEvidence(SessionQueuedMessage row) =>
        new(
            row.Id,
            row.AgentSessionId,
            row.Origin,
            row.Status,
            row.CreatedAt,
            row.ExecutionTaskId,
            row.SourceTaskId,
            row.SourceLandNotificationId,
            row.ConversationKey,
            row.ContentDigest,
            row.Body,
            row.RemoteSpillBody,
            row.RemoteSpillRelativePath,
            row.RulesCoveredByMessageId,
            row.DeliveryAttempts,
            row.DeliveryVerdict,
            row.LastDeliveryStartedAt,
            row.SentAt,
            row.CanceledAt,
            row.LastDeliveryGeneration,
            row.LastDeliveryBaselineSequence);
}

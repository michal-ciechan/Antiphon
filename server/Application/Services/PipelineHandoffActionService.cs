using System.Globalization;
using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Explicit caller accounting only. No scanner, timer, delivery receipt or read creates a
/// consumption record. Child admission is a separate operation owned by the admission boundary.
/// </summary>
public sealed class PipelineHandoffActionService(AppDbContext db, TimeProvider clock, IServiceScopeFactory? scopes = null)
{
    public async Task<PipelineHandoffActionDto> RecordAsync(Guid predecessorId,
        PipelineHandoffActionRequest request, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // PostgreSQL stores microseconds; replay must compare the canonical persisted value.
        if (request.NextObservationAt is { } nextObservation)
            request = request with { NextObservationAt = new DateTime(nextObservation.Ticks - nextObservation.Ticks % 10, nextObservation.Kind) };
        Validate(request, actor, now);
        var predecessor = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == predecessorId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), predecessorId);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The shared admission/close order starts at the card. Locking the predecessor also
        // serializes the first insert before an action row exists.
        if (predecessor.CardId is Guid cardId)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cards\" WHERE \"Id\" = {cardId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {predecessorId} FOR UPDATE", ct);
        var current = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == predecessorId, ct);
        if (current.CardId != predecessor.CardId || current.ProjectId != predecessor.ProjectId
            || current.Attempt != request.Attempt || current.Status != AgentTaskStatus.Succeeded
            || current.NextStage is not { } next || !PipelineHandoff.TryToStageRole(next, out var role)
            || role != request.NextStage)
            throw Refuse("predecessor_changed");
        var settlement = await db.AgentTaskEvents.AsNoTracking().SingleOrDefaultAsync(e =>
            e.Id == request.SettlementEventId && e.AgentTaskId == predecessorId
            && e.Type == AgentTaskEventType.Completed && e.At == current.CompletedAt, ct)
            ?? throw Refuse("settlement_changed");
        if (await TaskSuccessorFence.ReasonAsync(db, current, ct) is not null)
            throw Refuse("successor_held");
        await RequireWorkflowAsync(current, request, ct);

        var action = await db.PipelineHandoffActions.SingleOrDefaultAsync(a =>
            a.PredecessorTaskId == predecessorId && a.Attempt == request.Attempt
            && a.SettlementEventId == request.SettlementEventId && a.NextStage == request.NextStage
            && a.WorkflowRunId == request.WorkflowRunId
            && a.PipelineDefinitionRevisionId == request.PipelineDefinitionRevisionId
            && a.CurrentStageId == request.CurrentStageId && a.WorkflowUpdatedAt == request.WorkflowUpdatedAt, ct);
        if (action is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PipelineHandoffActions\" WHERE \"Id\" = {action.Id} FOR UPDATE", ct);
            await db.Entry(action).ReloadAsync(ct);
        }
        var last = action is null ? null : await db.PipelineHandoffObservations.AsNoTracking()
            .Where(o => o.ActionId == action.Id).OrderByDescending(o => o.Sequence).FirstOrDefaultAsync(ct);
        if (last is not null && request.ExpectedObservationSequence == last.Sequence - 1
            && last.ReasonCode == request.ReasonCode && last.Detail == request.Detail
            && last.NextObservationAt == request.NextObservationAt && last.Actor == actor
            && SameReceipt(action!, request.Receipt))
        {
            await transaction.CommitAsync(ct);
            return await ReadAsync(action!, ct);
        }
        if (request.ExpectedObservationSequence != (last?.Sequence ?? 0))
            throw Refuse("observation_changed");
        if (request.NextObservationAt is { } due && due <= now)
            throw new ValidationException("nextObservationAt", "A new observation must be scheduled in the future.");
        if (action?.ChildTaskId is not null) throw Refuse("already_requested");
        action ??= new PipelineHandoffAction
        {
            Id = Guid.NewGuid(), PredecessorTaskId = predecessorId, Attempt = request.Attempt,
            SettlementEventId = request.SettlementEventId, NextStage = request.NextStage,
            CardId = current.CardId, ProjectId = current.ProjectId,
            WorkflowRunId = request.WorkflowRunId, PipelineDefinitionRevisionId = request.PipelineDefinitionRevisionId,
            CurrentStageId = request.CurrentStageId, WorkflowUpdatedAt = request.WorkflowUpdatedAt,
            SettledAt = settlement.At, CreatedAt = now, Actor = actor, RecipientSessionId = current.ParentSessionId,
        };
        if (db.Entry(action).State == EntityState.Detached) db.PipelineHandoffActions.Add(action);
        if (request.Receipt is { } receipt)
        {
            if (action.NotificationId is not null && !SameReceipt(action, receipt)) throw Refuse("receipt_changed");
            await BindReceiptAsync(action, current, receipt, ct);
        }
        else if (action.NotificationId is not null) throw Refuse("receipt_changed");
        action.ConsumedAt ??= now;
        if (request.ReasonCode == "eligible") action.EligibleAt ??= now;
        action.ReasonCode = request.ReasonCode;
        action.NextObservationAt = request.NextObservationAt;
        db.PipelineHandoffObservations.Add(new PipelineHandoffObservation
        {
            Id = Guid.NewGuid(), ActionId = action.Id, Sequence = (last?.Sequence ?? 0) + 1,
            ReasonCode = request.ReasonCode, Detail = request.Detail, Actor = actor,
            ObservedAt = now, NextObservationAt = request.NextObservationAt,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ReadAsync(action, ct);
    }

    public async Task<IReadOnlyList<PipelineHandoffActionDto>> GetAsync(Guid predecessorId, CancellationToken ct)
    {
        if (!await db.AgentTasks.AnyAsync(t => t.Id == predecessorId, ct))
            throw new NotFoundException(nameof(AgentTask), predecessorId);
        var actions = await db.PipelineHandoffActions.AsNoTracking().Where(a => a.PredecessorTaskId == predecessorId)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
        var results = new List<PipelineHandoffActionDto>();
        foreach (var action in actions) results.Add(await ReadAsync(action, ct));
        return results;
    }

    public async Task<AgentTaskCreatedDto> RequestChildAsync(Guid predecessorId, Guid actionId,
        PipelineHandoffChildRequest request, AgentTaskService.Caller caller, string actor, CancellationToken ct)
    {
        var action = await db.PipelineHandoffActions.AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == actionId && a.PredecessorTaskId == predecessorId, ct)
            ?? throw new NotFoundException("PipelineHandoffAction", actionId);
        if (!caller.IsOperator && (action.RecipientSessionId is null || action.RecipientSessionId != caller.SessionId))
            throw new ForbiddenException("Only the completion recipient may request its child.", "handoff_recipient_denied");
        try
        {
            // This is the explicit caller pilot. A tracker/scheduled trigger needs its own
            // activated policy and is not granted authority by this endpoint.
            if (request.Trigger != "caller") throw Refuse("trigger_held");
            if (!request.AcceptSpend) throw Refuse("spend_held");
            if (request.Task is null) throw new ValidationException("task", "A child request is required.");
            if (action.ChildTaskId is Guid childId)
            {
                if (action.RequestDigest != AgentTaskService.HandoffRequestDigest(request.Task)) throw Refuse("request_changed");
                var child = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == childId, ct);
                if (child is null || child.CardId != action.CardId || child.ProjectId != action.ProjectId
                    || child.Role != action.NextStage) throw Refuse("child_mismatch");
                // Recovery of a committed request must not spend again or re-run launch gates.
                return new(child.Id, DelegationReportFormatter.Short(child.Id), child.Status, child.ModelLevel,
                    null, child.AgentKind, CardId: child.CardId, RequiredPlatform: child.RequiredPlatform,
                    RunnerId: RunnerRequestIntent.DisplayRunnerId(child.RunnerId));
            }
            await using var scope = (scopes ?? throw new InvalidOperationException("Child admission requires an owned scope."))
                .CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(request.Task, caller, actionId, ct, acceptSpend: request.AcceptSpend);
        }
        catch (HttpException error) when (error.StatusCode is 409 or 422)
        {
            // Create owns its transaction; persist a refusal only after that scope has rolled
            // back. A committed child (including a lost response) is never relabeled as held.
            await RecordRefusalAsync(action, error, actor, ct);
            throw;
        }
    }

    private async Task RecordRefusalAsync(PipelineHandoffAction observed, HttpException error, string actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (observed.CardId is Guid cardId)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cards\" WHERE \"Id\" = {cardId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PipelineHandoffActions\" WHERE \"Id\" = {observed.Id} FOR UPDATE", ct);
        var action = await db.PipelineHandoffActions.SingleAsync(a => a.Id == observed.Id, ct);
        await db.Entry(action).ReloadAsync(ct);
        if (action.ChildTaskId is not null) { await tx.CommitAsync(ct); return; }
        var code = error.Code ?? "validation_failed";
        var reason = code switch
        {
            "handoff_dependency_held" => "dependency",
            "handoff_overlap_held" => "overlap",
            "handoff_capacity_held" or "concurrency_limit" => "capacity",
            "subscription_quota_low" => "quota",
            "handoff_spend_held" or "handoff_trigger_held" or "handoff_decision_held" or "handoff_override_held" => "decision",
            _ when error is ValidationException validation && validation.Errors.Keys.Any(k =>
                k.Equals("RunnerId", StringComparison.OrdinalIgnoreCase) || k.Equals("RequiredPlatform", StringComparison.OrdinalIgnoreCase)) => "placement",
            _ => "evidence",
        };
        var sequence = await db.PipelineHandoffObservations.Where(o => o.ActionId == action.Id)
            .Select(o => (long?)o.Sequence).MaxAsync(ct) ?? 0;
        var now = clock.GetUtcNow().UtcDateTime;
        action.ReasonCode = reason;
        action.NextObservationAt = now.AddMinutes(5);
        db.PipelineHandoffObservations.Add(new PipelineHandoffObservation
        {
            Id = Guid.NewGuid(), ActionId = action.Id, Sequence = sequence + 1, Actor = actor,
            ReasonCode = reason, Detail = "Child request refused: " + code,
            ObservedAt = now, NextObservationAt = action.NextObservationAt,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<PipelineHandoffActionDto> ReadAsync(PipelineHandoffAction action, CancellationToken ct)
    {
        // Always fetch a detached snapshot: projection must not write cached task clocks back.
        var snapshot = await db.PipelineHandoffActions.AsNoTracking().SingleAsync(a => a.Id == action.Id, ct);
        var child = snapshot.ChildTaskId is { } childId
            ? await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == childId, ct) : null;
        if (snapshot.ChildTaskId is not null && (child is null || child.CardId != snapshot.CardId
            || child.ProjectId != snapshot.ProjectId || child.Role != snapshot.NextStage))
            throw Refuse("child_mismatch");
        snapshot.ChildCreatedAt = child?.CreatedAt;
        snapshot.ChildDispatchedAt = child?.DispatchedAt;
        var history = await db.PipelineHandoffObservations.AsNoTracking().Where(o => o.ActionId == action.Id)
            .OrderBy(o => o.Sequence).ToListAsync(ct);
        bool? timely = snapshot.EligibleAt is { } eligible && child is not null
            ? child.CreatedAt >= eligible && child.CreatedAt - eligible <= TimeSpan.FromMinutes(5) : null;
        var unexplained = child is null && snapshot.ReasonCode == "eligible" && snapshot.EligibleAt is { } since
            && clock.GetUtcNow().UtcDateTime - since > TimeSpan.FromMinutes(15);
        return new(snapshot, history, timely, unexplained);
    }

    private async Task RequireWorkflowAsync(AgentTask predecessor, PipelineHandoffActionRequest request, CancellationToken ct)
    {
        var card = predecessor.CardId is { } id
            ? await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) : null;
        if (card?.ActiveWorkflowRunId != request.WorkflowRunId) throw Refuse("workflow_changed");
        if (request.WorkflowRunId is { } runId)
        {
            var run = await db.CardWorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null || run.CardId != predecessor.CardId
                || run.PipelineDefinitionRevisionId != request.PipelineDefinitionRevisionId
                || run.CurrentStageId != request.CurrentStageId || run.UpdatedAt != request.WorkflowUpdatedAt)
                throw Refuse("workflow_changed");
        }
        else if (request.PipelineDefinitionRevisionId is not null || request.CurrentStageId is not null
            || request.WorkflowUpdatedAt is not null) throw Refuse("workflow_changed");
    }

    private async Task BindReceiptAsync(PipelineHandoffAction action, AgentTask predecessor,
        PipelineHandoffReceiptReference receipt, CancellationToken ct)
    {
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == receipt.NotificationId, ct);
        var queue = await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(q => q.Id == receipt.QueueId, ct);
        var generation = await db.AgentSessions.AsNoTracking().Where(s => s.Id == receipt.RecipientSessionId)
            .Select(s => s.StartedAt).SingleOrDefaultAsync(ct);
        var rendering = TaskCompletionNotification.TryReadDelivery(note?.CompletionDeliveryJson);
        if (note is null || queue is null || rendering is null || note.TaskId != predecessor.Id
            || note.SourceEventId != action.SettlementEventId || note.Kind != LandNotificationKind.TaskCompletion
            || note.State != LandNotificationState.Confirmed || note.ConfirmedAt is null
            || note.QueueMessageId != queue.Id || queue.SourceLandNotificationId != note.Id
            || queue.SourceTaskId != predecessor.Id || note.ParentSessionId != receipt.RecipientSessionId
            || predecessor.ParentSessionId != receipt.RecipientSessionId || queue.AgentSessionId != receipt.RecipientSessionId
            || queue.DeliveryAttempts == 0 || queue.LastDeliveryGeneration != receipt.RecipientGeneration
            || generation != receipt.RecipientGeneration || note.ConfirmingPromptSequence != receipt.ReceiptSequence
            || !rendering.MemberQueueIds.Contains(queue.Id)
            || !string.Equals(rendering.WireSha256, receipt.WireDigest, StringComparison.OrdinalIgnoreCase))
            throw Refuse("foreign_receipt");
        var prompts = LandNoteReceipt.Prompts(db.TranscriptEntries.AsNoTracking(), receipt.RecipientSessionId,
            note.IsLegacy, note.Kind, queue.LastDeliveryBaselineSequence, queue.LastDeliveryStartedAt, 0);
        var prompt = prompts is null ? null : await prompts.SingleOrDefaultAsync(p => p.Sequence == receipt.ReceiptSequence, ct);
        if (prompt?.Text is null || !LandNoteReceipt.IsReceipt(rendering.WireText, prompt.Text))
            throw Refuse("foreign_receipt");
        if (rendering.SpillPath is { } path)
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (!string.Equals(hash, rendering.SpillSha256, StringComparison.OrdinalIgnoreCase)) throw Refuse("foreign_receipt");
            }
            catch (IOException) { throw Refuse("foreign_receipt"); }
            catch (UnauthorizedAccessException) { throw Refuse("foreign_receipt"); }
        }
        action.NotificationId = note.Id; action.QueueId = queue.Id;
        action.RecipientSessionId = receipt.RecipientSessionId;
        action.RecipientGeneration = receipt.RecipientGeneration.ToString("O", CultureInfo.InvariantCulture);
        action.ReceiptSequence = receipt.ReceiptSequence; action.WireDigest = rendering.WireSha256;
        action.NotifiedAt = note.CreatedAt; action.ReceivedAt = note.ConfirmedAt;
    }

    private static bool SameReceipt(PipelineHandoffAction action, PipelineHandoffReceiptReference? receipt) =>
        receipt is null ? action.NotificationId is null : action.NotificationId == receipt.NotificationId
        && action.QueueId == receipt.QueueId && action.RecipientSessionId == receipt.RecipientSessionId
        && action.RecipientGeneration == receipt.RecipientGeneration.ToString("O", CultureInfo.InvariantCulture)
        && action.ReceiptSequence == receipt.ReceiptSequence
        && string.Equals(action.WireDigest, receipt.WireDigest, StringComparison.OrdinalIgnoreCase);

    private static void Validate(PipelineHandoffActionRequest request, string actor, DateTime now)
    {
        var hold = request.ReasonCode is "dependency" or "decision" or "capacity" or "overlap"
            or "priority" or "evidence" or "quota" or "placement";
        if ((!hold && request.ReasonCode is not ("consumed" or "eligible"))
            || string.IsNullOrWhiteSpace(request.Detail) || request.Detail.Length > 2000
            || string.IsNullOrWhiteSpace(actor) || actor.Length > 200
            || request.Attempt < 0 || request.ExpectedObservationSequence < 0 || request.SettlementEventId == Guid.Empty
            || (hold && request.NextObservationAt is null)
            || (request.NextObservationAt is { Kind: not DateTimeKind.Utc })
            || (request.WorkflowUpdatedAt is { Kind: not DateTimeKind.Utc })
            || (request.Receipt is { RecipientGeneration.Kind: not DateTimeKind.Utc }))
            throw new ValidationException("request", "A current handoff, bounded reason and future UTC observation time for a hold are required.");
    }

    private static ConflictException Refuse(string code) => new("The handoff observation is stale or unproven.", "handoff_" + code);
}

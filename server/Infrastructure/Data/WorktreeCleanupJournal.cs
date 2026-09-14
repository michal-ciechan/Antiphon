using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>Independent boundary commits. Never a source of removal authority.</summary>
public sealed class WorktreeCleanupJournal(IServiceScopeFactory scopes, TimeProvider clock) : IWorktreeCleanupJournal
{
    public async Task<WorktreeCleanupAttempt> GetOrCreateAsync(WorktreeCleanupIdentity identity, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.WorktreeCleanupAttempts.SingleOrDefaultAsync(a => a.RequestId == identity.RequestId, ct);
        if (row is not null) { ValidateIdentity(row, identity); return row; }
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == identity.RequestId, ct);
        var operation = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == identity.OperationId, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == identity.TaskId, ct);
        if (request.TaskId != identity.TaskId || operation.TaskId != identity.TaskId
            || task.CurrentLandRequestId != request.Id || !request.IsPending
            || request.LandingOperationId is Guid bound && bound != operation.Id
            || operation.RepositoryPath != identity.RepositoryPath || operation.WorktreePath != identity.WorktreePath
            || operation.CommonDirectory != identity.CommonDirectory || operation.GitDirectory != identity.GitDirectory
            || operation.SourceFullRef != identity.SourceFullRef || operation.TargetFullRef != identity.TargetFullRef
            || operation.VerifiedSourceSha != identity.SourceSha || operation.TargetBeforeSha != identity.TargetSha)
            throw new InvalidOperationException("cleanup_attempt_identity_mismatch");
        row = new WorktreeCleanupAttempt {
            Id = Guid.NewGuid(), RequestId = identity.RequestId, OperationId = identity.OperationId, TaskId = identity.TaskId,
            RepositoryPath = identity.RepositoryPath, WorktreePath = identity.WorktreePath,
            CommonDirectory = identity.CommonDirectory, GitDirectory = identity.GitDirectory,
            SourceFullRef = identity.SourceFullRef, TargetFullRef = identity.TargetFullRef,
            SourceSha = identity.SourceSha, TargetSha = identity.TargetSha, CreatedAt = Now() };
        db.WorktreeCleanupAttempts.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await using var freshScope = scopes.CreateAsyncScope();
            var freshDb = freshScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var committed = await freshDb.WorktreeCleanupAttempts.AsNoTracking()
                .SingleOrDefaultAsync(a => a.RequestId == identity.RequestId, ct);
            if (committed is null) throw;
            ValidateIdentity(committed, identity);
            return committed;
        }
        return row;
    }

    public async Task<WorktreeCleanupAttempt> ReadAsync(WorktreeCleanupContext context, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.WorktreeCleanupAttempts.AsNoTracking().SingleAsync(a => a.Id == context.AttemptId, ct);
        ValidateContext(row, context);
        return row;
    }

    public async Task<WorktreeCleanupEvidence> ReadEvidenceAsync(Guid operationId, Guid requestId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var current = await db.WorktreeCleanupAttempts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.OperationId == operationId && a.RequestId == requestId, ct);
        var capture = await db.WorktreeCleanupAttempts.AsNoTracking()
            .Where(a => a.OperationId == operationId && a.CaptureState != WorktreeCleanupCaptureState.NotNeeded)
            .OrderByDescending(a => a.RequestId == requestId).ThenByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        return new(current?.Id, capture is null ? null : new WorktreeCleanupPresentation().Reference(capture, capture.RequestId != requestId));
    }

    public async Task<bool> ConsumeSlotAsync(WorktreeCleanupContext context, Guid commandId, bool retry, CancellationToken ct)
    {
        if (commandId == Guid.Empty) throw new ArgumentException("cleanup_command_identity_required");
        var row = await UpdateAsync(context, a => {
            if (a.FinalizedAt is not null) throw new InvalidOperationException("cleanup_attempt_finalized");
            if (retry)
            {
                if (a.InitialCommandId is null || a.InitialCompletedAt is null
                    || a.CaptureState != WorktreeCleanupCaptureState.Captured || a.CaptureJson is null)
                    throw new InvalidOperationException("cleanup_retry_capture_required");
                if (a.RetryCommandId is not null) return;
                a.RetryCommandId = commandId; a.RetryIntentAt = Now();
            }
            else
            {
                if (a.InitialCommandId is not null) return;
                a.InitialCommandId = commandId; a.InitialIntentAt = Now();
            }
        }, a => (retry ? a.RetryCommandId : a.InitialCommandId) == commandId, ct);
        return (retry ? row.RetryCommandId : row.InitialCommandId) == commandId;
    }

    public Task RecordOutcomeAsync(WorktreeCleanupContext context, WorktreeGitOutcome outcome,
        bool retry, bool failure, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(outcome);
        if (Encoding.UTF8.GetByteCount(json) > 2048) throw new ArgumentException("cleanup_git_outcome_too_large");
        return UpdateAsync(context, a => {
            if ((retry ? a.RetryCommandId : a.InitialCommandId) is null)
                throw new InvalidOperationException("cleanup_command_intent_required");
            if (a.FinalizedAt is not null) throw new InvalidOperationException("cleanup_attempt_finalized");
            if (retry) a.RetryCompletedAt ??= outcome.At; else a.InitialCompletedAt ??= outcome.At;
            a.LastGitOutcomeJson = json;
            if (failure && a.FirstGitFailureJson is null)
            {
                a.FirstGitFailureJson = json;
                a.CaptureState = WorktreeCleanupCaptureState.Pending;
                a.CaptureAt = outcome.At;
            }
        }, a => a.LastGitOutcomeJson == json && (!failure || a.FirstGitFailureJson is not null), ct);
    }

    public Task CaptureAsync(WorktreeCleanupContext context, WorktreeCleanupCapture capture, CancellationToken ct)
    {
        if (capture.Id != context.AttemptId || capture.RequestId != context.RequestId
            || capture.OperationId != context.OperationId || capture.TaskId != context.TaskId)
            throw new InvalidOperationException("cleanup_capture_identity_mismatch");
        var presentation = new WorktreeCleanupPresentation();
        var json = presentation.Serialize(capture);
        var summary = presentation.Summary(JsonSerializer.Deserialize<WorktreeCleanupCapture>(json)!);
        return UpdateAsync(context, a => {
            if (a.CaptureState == WorktreeCleanupCaptureState.Captured)
            {
                if (a.CaptureJson != json) throw new InvalidOperationException("cleanup_capture_immutable");
                return;
            }
            if (a.CaptureState != WorktreeCleanupCaptureState.Pending || a.FirstGitFailureJson is null
                || a.FirstGitFailureJson != JsonSerializer.Serialize(capture.GitFailure) || a.FinalizedAt is not null)
                throw new InvalidOperationException("cleanup_failure_checkpoint_required");
            a.CaptureJson = json; a.Summary = summary;
            a.CaptureState = WorktreeCleanupCaptureState.Captured;
        }, a => a.CaptureState == WorktreeCleanupCaptureState.Captured && a.CaptureJson == json, ct);
    }

    public Task InterruptAsync(WorktreeCleanupContext context, string reason, CancellationToken ct) =>
        UpdateAsync(context, a => {
            if (a.InitialCommandId is not null && a.CaptureState is WorktreeCleanupCaptureState.Pending or WorktreeCleanupCaptureState.NotNeeded)
            {
                a.CaptureState = WorktreeCleanupCaptureState.Interrupted;
                a.Summary = $"capture={a.Id:N} at {a.CaptureAt ?? a.InitialIntentAt:O}; Interrupted/{WorktreeCleanupPresentation.Clip(reason, 100)}";
            }
            a.RetryReason = WorktreeCleanupPresentation.Clip(reason, 100);
        }, a => a.RetryReason == WorktreeCleanupPresentation.Clip(reason, 100), ct);

    public Task DecideAsync(WorktreeCleanupContext context, string reason, CancellationToken ct) =>
        UpdateAsync(context, a => a.RetryReason = WorktreeCleanupPresentation.Clip(reason, 100),
            a => a.RetryReason == WorktreeCleanupPresentation.Clip(reason, 100), ct);

    private async Task<WorktreeCleanupAttempt> UpdateAsync(WorktreeCleanupContext context,
        Action<WorktreeCleanupAttempt> update, Func<WorktreeCleanupAttempt, bool> committed, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.WorktreeCleanupAttempts.SingleAsync(a => a.Id == context.AttemptId, ct);
        ValidateContext(row, context);
        update(row);
        row.ConcurrencyToken = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            var actual = await ReadAsync(context, ct);
            if (!committed(actual)) throw;
            return actual;
        }
        return row;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static void ValidateContext(WorktreeCleanupAttempt a, WorktreeCleanupContext context)
    {
        if (a.SchemaVersion != 1) throw new InvalidOperationException("cleanup_schema_unsupported");
        if (a.Id != context.AttemptId || a.RequestId != context.RequestId
            || a.OperationId != context.OperationId || a.TaskId != context.TaskId)
            throw new InvalidOperationException("cleanup_context_mismatch");
    }

    private static void ValidateIdentity(WorktreeCleanupAttempt a, WorktreeCleanupIdentity i)
    {
        ValidateContext(a, new(a.Id, i.RequestId, i.OperationId, i.TaskId));
        if (a.RepositoryPath != i.RepositoryPath || a.WorktreePath != i.WorktreePath
            || a.CommonDirectory != i.CommonDirectory || a.GitDirectory != i.GitDirectory
            || a.SourceFullRef != i.SourceFullRef || a.TargetFullRef != i.TargetFullRef
            || a.SourceSha != i.SourceSha || a.TargetSha != i.TargetSha)
            throw new InvalidOperationException("cleanup_attempt_identity_mismatch");
    }
}

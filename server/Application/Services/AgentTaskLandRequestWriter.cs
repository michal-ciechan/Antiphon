using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal enum LandSourceCheckpointDisposition
{
    Applied,
    StaleRequest,
    SourceStateChanged,
}

internal sealed record LandSourceCheckpointBaseline(
    Guid RequestId,
    Guid TaskId,
    int Attempt,
    int TaskLandAttempt,
    DateTime? RequestedAt,
    int SchemaVersion,
    string? ExpectedSourceSha,
    Guid? ReviewEvidenceId,
    LandApprovalKind ApprovalKind,
    DateTime? ApprovedAt,
    string? VerifyFilter,
    string? SourceFullRefSnapshot,
    string? RepositoryPathSnapshot,
    string? WorktreePathSnapshot,
    string? TargetFullRefSnapshot,
    LandSourceResolutionState SourceResolutionState,
    string? LocalBeforeSha,
    string? RemoteSourceSha,
    string? RemoteSourceRef,
    string? RemoteSourceFingerprint,
    string? SourceObservationRef,
    DateTime? SourceObservedAt,
    string? ResolvedSourceSha,
    string? CandidateSourceSha,
    LandSourceRelationship SourceRelationship,
    string? SourceCommonDirectory,
    string? SourceWorktreePath,
    string? SourceGitDirectory,
    string? SourceRefusalReason,
    string? SourceDiagnosticCommand,
    int? SourceDiagnosticExitCode,
    string? SourceDiagnosticCode,
    string? SourceDiagnosticExceptionType,
    int? SourceAdvanceChildProcessId,
    long? SourceAdvanceChildStartTicks,
    string? SourceAdvanceChildOperation,
    LandRecoveryMode RecoveryMode,
    AgentTaskStatus? RecoveryOwnerStatus,
    Guid? RecoverySourceTaskId,
    string? RecoverySourceFullRef,
    string? RecoverySourceFingerprint,
    Guid? SupersedesRequestId,
    string? RecoveryStartBaseSha,
    string? RecoveryLocalBeforeSha,
    string? RecoveryOwnerRemoteBeforeSha,
    string? RecoveryOwnerRemoteAfterSha,
    string? RecoveryRelationship,
    DateTime? RecoveryAdoptedAt,
    Guid? LandingOperationId,
    Guid? ActiveLandingId)
{
    public static LandSourceCheckpointBaseline From(AgentTaskLandRequest request, AgentTask task) => new(
        request.Id, request.TaskId, request.Attempt, task.LandAttempt, request.RequestedAt,
        request.SchemaVersion, request.ExpectedSourceSha, request.ReviewEvidenceId, request.ApprovalKind,
        request.ApprovedAt, request.VerifyFilter, request.SourceFullRefSnapshot, request.RepositoryPathSnapshot,
        request.WorktreePathSnapshot, request.TargetFullRefSnapshot, request.SourceResolutionState,
        request.LocalBeforeSha, request.RemoteSourceSha, request.RemoteSourceRef, request.RemoteSourceFingerprint,
        request.SourceObservationRef, request.SourceObservedAt, request.ResolvedSourceSha, request.CandidateSourceSha,
        request.SourceRelationship, request.SourceCommonDirectory, request.SourceWorktreePath, request.SourceGitDirectory,
        request.SourceRefusalReason, request.SourceDiagnosticCommand, request.SourceDiagnosticExitCode,
        request.SourceDiagnosticCode, request.SourceDiagnosticExceptionType, request.SourceAdvanceChildProcessId,
        request.SourceAdvanceChildStartTicks, request.SourceAdvanceChildOperation,
        request.RecoveryMode, request.RecoveryOwnerStatus, request.RecoverySourceTaskId,
        request.RecoverySourceFullRef, request.RecoverySourceFingerprint, request.SupersedesRequestId,
        request.RecoveryStartBaseSha, request.RecoveryLocalBeforeSha, request.RecoveryOwnerRemoteBeforeSha,
        request.RecoveryOwnerRemoteAfterSha, request.RecoveryRelationship, request.RecoveryAdoptedAt,
        request.LandingOperationId,
        task.ActiveLandingId);
}

internal sealed record LandSourceCheckpointPatch(
    LandSourceResolutionState SourceResolutionState,
    string? LocalBeforeSha,
    string? RemoteSourceSha,
    string? RemoteSourceRef,
    string? RemoteSourceFingerprint,
    string? SourceObservationRef,
    DateTime? SourceObservedAt,
    string? ResolvedSourceSha,
    string? CandidateSourceSha,
    LandSourceRelationship SourceRelationship,
    string? SourceCommonDirectory,
    string? SourceWorktreePath,
    string? SourceGitDirectory,
    string? SourceRefusalReason,
    string? SourceDiagnosticCommand,
    int? SourceDiagnosticExitCode,
    string? SourceDiagnosticCode,
    string? SourceDiagnosticExceptionType,
    int? SourceAdvanceChildProcessId,
    long? SourceAdvanceChildStartTicks,
    string? SourceAdvanceChildOperation,
    string? RecoverySourceFingerprint,
    string? RecoveryLocalBeforeSha,
    string? RecoveryOwnerRemoteBeforeSha,
    string? RecoveryOwnerRemoteAfterSha,
    string? RecoveryRelationship,
    DateTime? RecoveryAdoptedAt)
{
    public static LandSourceCheckpointPatch From(AgentTaskLandRequest request) => new(
        request.SourceResolutionState, request.LocalBeforeSha, request.RemoteSourceSha, request.RemoteSourceRef,
        request.RemoteSourceFingerprint, request.SourceObservationRef, request.SourceObservedAt,
        request.ResolvedSourceSha, request.CandidateSourceSha, request.SourceRelationship,
        request.SourceCommonDirectory, request.SourceWorktreePath, request.SourceGitDirectory,
        request.SourceRefusalReason, request.SourceDiagnosticCommand, request.SourceDiagnosticExitCode,
        request.SourceDiagnosticCode, request.SourceDiagnosticExceptionType, request.SourceAdvanceChildProcessId,
        request.SourceAdvanceChildStartTicks, request.SourceAdvanceChildOperation,
        request.RecoverySourceFingerprint, request.RecoveryLocalBeforeSha, request.RecoveryOwnerRemoteBeforeSha,
        request.RecoveryOwnerRemoteAfterSha, request.RecoveryRelationship, request.RecoveryAdoptedAt);
}

internal sealed record LandSourceCheckpointResult(
    LandSourceCheckpointDisposition Disposition,
    LandSourceCheckpointBaseline Baseline);

internal sealed class AgentTaskLandRequestWriter(AppDbContext db, TimeProvider clock)
{
    public async Task<LandSourceCheckpointResult> ApplyAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, LandSourceCheckpointPatch patch, CancellationToken ct)
    {
        var owns = db.Database.CurrentTransaction is null;
        await using var transaction = owns ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockTaskAsync(task.Id, ct);
        await db.Entry(task).ReloadAsync(ct);
        await db.Entry(request).ReloadAsync(ct);

        if (IsStale(task, request, baseline))
            return new(LandSourceCheckpointDisposition.StaleRequest, baseline);
        if (!SameSourceProgress(request, task, baseline))
            return new(LandSourceCheckpointDisposition.SourceStateChanged, LandSourceCheckpointBaseline.From(request, task));

        var adoptedNow = request.RecoveryAdoptedAt is null && patch.RecoveryAdoptedAt is not null;
        ApplyPatch(request, patch);
        request.ConcurrencyToken = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;
        if (adoptedNow)
        {
            var detail = $"Reviewed source {request.ExpectedSourceSha} from task {request.RecoverySourceTaskId:N} "
                + $"({request.RecoverySourceFullRef}) adopted for owner {task.Id:N}; "
                + $"local-before={request.RecoveryLocalBeforeSha}; remote-before={request.RecoveryOwnerRemoteBeforeSha}; "
                + $"remote-after={request.RecoveryOwnerRemoteAfterSha}; relationship={request.RecoveryRelationship}; "
                + $"review={request.ReviewEvidenceId:N}.";
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = task.Id, LandRequestId = request.Id,
                Type = AgentTaskEventType.SourceAdopted, Detail = detail, At = now,
            });
            if (request.RecoverySourceTaskId is Guid sourceId && sourceId != task.Id)
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(), AgentTaskId = sourceId,
                    Type = AgentTaskEventType.SourceAdopted,
                    Detail = $"Source {request.ExpectedSourceSha} adopted into owner {task.Id:N} land request {request.Id:N}.",
                    At = now,
                });
        }
        if (now > request.LastProgressAt) request.LastProgressAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        await db.Entry(request).ReloadAsync(ct);
        await db.Entry(task).ReloadAsync(ct);
        return new(LandSourceCheckpointDisposition.Applied, LandSourceCheckpointBaseline.From(request, task));
    }

    public async Task<LandSourceCheckpointDisposition> AttachOperationAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, AgentTaskLanding operation, AgentTaskLanding? previous,
        CancellationToken ct)
    {
        var owns = db.Database.CurrentTransaction is null;
        await using var transaction = owns ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockTaskAsync(task.Id, ct);
        await db.Entry(task).ReloadAsync(ct);
        await db.Entry(request).ReloadAsync(ct);
        if (previous is not null) await db.Entry(previous).ReloadAsync(ct);

        if (IsStale(task, request, baseline))
            return LandSourceCheckpointDisposition.StaleRequest;
        if (task.ActiveLandingId != baseline.ActiveLandingId || request.LandingOperationId != baseline.LandingOperationId)
            return LandSourceCheckpointDisposition.StaleRequest;
        if (!SameSourceProgress(request, task, baseline))
            return LandSourceCheckpointDisposition.StaleRequest;

        if (previous is not null && previous.Active)
        {
            previous.Active = false;
            previous.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }

        db.AgentTaskLandings.Add(operation);
        task.ActiveLandingId = operation.Id;
        request.LandingOperationId = operation.Id;
        task.ConcurrencyToken = Guid.NewGuid();
        request.ConcurrencyToken = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;
        if (now > request.LastProgressAt) request.LastProgressAt = now;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        await db.Entry(request).ReloadAsync(ct);
        await db.Entry(task).ReloadAsync(ct);
        return LandSourceCheckpointDisposition.Applied;
    }

    public Task LockTaskAsync(Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {id} FOR UPDATE", ct);

    private static bool IsStale(AgentTask task, AgentTaskLandRequest request, LandSourceCheckpointBaseline baseline)
    {
        if (request.Id != baseline.RequestId || task.Id != baseline.TaskId) return true;
        if (!request.IsPending || request.TerminalEventId is not null) return true;
        if (task.CurrentLandRequestId != request.Id || task.LandRequestedAt is null) return true;
        if (!LandApproval.RequestStatusEligible(task, request)) return true;
        if (request.Attempt != baseline.Attempt || task.LandAttempt != baseline.TaskLandAttempt) return true;
        if (task.LandRequestedAt != baseline.RequestedAt || request.RequestedAt != baseline.RequestedAt) return true;
        if (request.SchemaVersion != baseline.SchemaVersion
            || request.ExpectedSourceSha != baseline.ExpectedSourceSha
            || request.ReviewEvidenceId != baseline.ReviewEvidenceId
            || request.ApprovalKind != baseline.ApprovalKind
            || request.ApprovedAt != baseline.ApprovedAt
            || request.VerifyFilter != baseline.VerifyFilter
            || request.SourceFullRefSnapshot != baseline.SourceFullRefSnapshot
            || request.RepositoryPathSnapshot != baseline.RepositoryPathSnapshot
            || request.WorktreePathSnapshot != baseline.WorktreePathSnapshot
            || request.TargetFullRefSnapshot != baseline.TargetFullRefSnapshot)
            return true;
        if (request.RecoveryMode != baseline.RecoveryMode
            || request.RecoveryOwnerStatus != baseline.RecoveryOwnerStatus
            || request.RecoverySourceTaskId != baseline.RecoverySourceTaskId
            || request.RecoverySourceFullRef != baseline.RecoverySourceFullRef
            || request.SupersedesRequestId != baseline.SupersedesRequestId
            || request.RecoveryStartBaseSha != baseline.RecoveryStartBaseSha)
            return true;
        return false;
    }

    private static bool SameSourceProgress(AgentTaskLandRequest request, AgentTask task,
        LandSourceCheckpointBaseline baseline) =>
        request.SourceResolutionState == baseline.SourceResolutionState
        && request.LocalBeforeSha == baseline.LocalBeforeSha
        && request.RemoteSourceSha == baseline.RemoteSourceSha
        && request.RemoteSourceRef == baseline.RemoteSourceRef
        && request.RemoteSourceFingerprint == baseline.RemoteSourceFingerprint
        && request.SourceObservationRef == baseline.SourceObservationRef
        && request.SourceObservedAt == baseline.SourceObservedAt
        && request.ResolvedSourceSha == baseline.ResolvedSourceSha
        && request.CandidateSourceSha == baseline.CandidateSourceSha
        && request.SourceRelationship == baseline.SourceRelationship
        && request.SourceCommonDirectory == baseline.SourceCommonDirectory
        && request.SourceWorktreePath == baseline.SourceWorktreePath
        && request.SourceGitDirectory == baseline.SourceGitDirectory
        && request.SourceRefusalReason == baseline.SourceRefusalReason
        && request.SourceDiagnosticCommand == baseline.SourceDiagnosticCommand
        && request.SourceDiagnosticExitCode == baseline.SourceDiagnosticExitCode
        && request.SourceDiagnosticCode == baseline.SourceDiagnosticCode
        && request.SourceDiagnosticExceptionType == baseline.SourceDiagnosticExceptionType
        && request.SourceAdvanceChildProcessId == baseline.SourceAdvanceChildProcessId
        && request.SourceAdvanceChildStartTicks == baseline.SourceAdvanceChildStartTicks
        && request.SourceAdvanceChildOperation == baseline.SourceAdvanceChildOperation
        && request.RecoverySourceFingerprint == baseline.RecoverySourceFingerprint
        && request.RecoveryLocalBeforeSha == baseline.RecoveryLocalBeforeSha
        && request.RecoveryOwnerRemoteBeforeSha == baseline.RecoveryOwnerRemoteBeforeSha
        && request.RecoveryOwnerRemoteAfterSha == baseline.RecoveryOwnerRemoteAfterSha
        && request.RecoveryRelationship == baseline.RecoveryRelationship
        && request.RecoveryAdoptedAt == baseline.RecoveryAdoptedAt
        && request.LandingOperationId == baseline.LandingOperationId
        && task.ActiveLandingId == baseline.ActiveLandingId;

    private static void ApplyPatch(AgentTaskLandRequest request, LandSourceCheckpointPatch patch)
    {
        request.SourceResolutionState = patch.SourceResolutionState;
        request.LocalBeforeSha = patch.LocalBeforeSha;
        request.RemoteSourceSha = patch.RemoteSourceSha;
        request.RemoteSourceRef = patch.RemoteSourceRef;
        request.RemoteSourceFingerprint = patch.RemoteSourceFingerprint;
        request.SourceObservationRef = patch.SourceObservationRef;
        request.SourceObservedAt = patch.SourceObservedAt;
        request.ResolvedSourceSha = patch.ResolvedSourceSha;
        request.CandidateSourceSha = patch.CandidateSourceSha;
        request.SourceRelationship = patch.SourceRelationship;
        request.SourceCommonDirectory = patch.SourceCommonDirectory;
        request.SourceWorktreePath = patch.SourceWorktreePath;
        request.SourceGitDirectory = patch.SourceGitDirectory;
        request.SourceRefusalReason = patch.SourceRefusalReason;
        request.SourceDiagnosticCommand = patch.SourceDiagnosticCommand;
        request.SourceDiagnosticExitCode = patch.SourceDiagnosticExitCode;
        request.SourceDiagnosticCode = patch.SourceDiagnosticCode;
        request.SourceDiagnosticExceptionType = patch.SourceDiagnosticExceptionType;
        request.SourceAdvanceChildProcessId = patch.SourceAdvanceChildProcessId;
        request.SourceAdvanceChildStartTicks = patch.SourceAdvanceChildStartTicks;
        request.SourceAdvanceChildOperation = patch.SourceAdvanceChildOperation;
        request.RecoverySourceFingerprint = patch.RecoverySourceFingerprint;
        request.RecoveryLocalBeforeSha = patch.RecoveryLocalBeforeSha;
        request.RecoveryOwnerRemoteBeforeSha = patch.RecoveryOwnerRemoteBeforeSha;
        request.RecoveryOwnerRemoteAfterSha = patch.RecoveryOwnerRemoteAfterSha;
        request.RecoveryRelationship = patch.RecoveryRelationship;
        request.RecoveryAdoptedAt = patch.RecoveryAdoptedAt;
    }
}

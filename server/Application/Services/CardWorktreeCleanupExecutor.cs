using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CardDone admission into the existing typed retirement executor.</summary>
public sealed class CardWorktreeCleanupExecutor(AppDbContext db, ILandingGit git,
    CardDoneArtifactPreservation artifacts, TaskWorktreeRetirementService retirement,
    WorkspaceUseAdmission admission, TimeProvider clock, IOptions<WorktreeResidueSettings> settings,
    IOptions<GitSettings> gitSettings, AgentTaskLandService land)
{
    internal Func<CancellationToken, Task>? BeforeIntentAsync { get; set; }
    internal Func<CancellationToken, Task>? AfterIntentAsync { get; set; }
    internal Func<CancellationToken, Task>? BeforeOutcomeAsync { get; set; }

    public async Task<WorktreeRemoval> TryAsync(Guid endpointId, Guid? runId, CancellationToken ct)
    {
        var endpoint = await db.CardWorktreeCleanupEndpoints.Include(e => e.Target).ThenInclude(t => t.Cleanup)
            .SingleAsync(e => e.Id == endpointId, ct);
        if (endpoint.State == CardWorktreeCleanupEndpointState.Complete)
            return new(endpoint.RegistrationRemoved == true, endpoint.DirectoryRemoved == true, endpoint.BranchRemoved == true, null);
        if (endpoint.OperationId is not null && endpoint.Target.RetirementId is Guid prior)
            return await ExecuteAsync(endpoint, await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == prior, ct), runId, ct);
        if (endpoint.OperationId is not null && endpoint.Target.LandingOperationId is not null)
            return await QueuePublicationAsync(endpoint, runId, ct);

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == endpoint.Target.TaskId, ct);
        var reason = await EligibilityAsync(task, endpoint, ct);
        if (reason is not null) return await RefuseAsync(endpoint, reason, ct);
        var publication = await ConfirmedPublicationAsync(task, endpoint, ct);
        var source = new LandSourceCoordinates(task.Id, endpoint.RepositoryPath, endpoint.WorktreePath,
            endpoint.SourceFullRef, WorkspaceReservationKey.NormalizeRef(task.MergeTargetRef ?? "master"));
        var inspected = await git.InspectAsync(source, LandInspectionScope.Full, ct);
        if (!inspected.Accepted) return await RefuseAsync(endpoint, inspected.Reason ?? "source_changed", ct);
        var snapshot = inspected.Snapshot!;
        var destination = await git.DestinationAsync(source.RepositoryPath, source.TargetFullRef, ct);
        var retirementId = Guid.NewGuid();
        if (publication is not null && (snapshot.HeadSha != publication.ExpectedDeletionSha
            || !WorkspaceReservationKey.PathsEqual(snapshot.CommonDirectory, publication.CommonDirectory)
            || !WorkspaceReservationKey.PathsEqual(snapshot.GitDirectory, publication.GitDirectory)
            || destination != new LandingDestination(publication.RemoteName, publication.DestinationFullRef, publication.RemoteFingerprint)))
            return await RefuseAsync(endpoint, "publication_identity_changed", ct);
        var observed = publication is null
            ? await git.ObserveRetirementAsync(source.RepositoryPath, destination, snapshot.HeadSha, retirementId, "cleanup-observed", ct)
            : await git.ObserveAsync(source.RepositoryPath, destination, publication.VerifiedSourceSha!,
                publication.RecoveryRefPrefix + "/cleanup-observed", ct);
        if (observed.Reason is not null || !observed.ContainsSource)
            return await RefuseAsync(endpoint, observed.Reason ?? "unlanded_work", ct);
        var roots = await CardDoneArtifactRoots.ReadAsync(db, endpoint.Target.CleanupId, ct);
        var priorRelease = await db.TaskWorktreeRetirements.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TaskId == task.Id && r.TaskAttempt == task.Attempt && r.Active, ct);
        var missingReviewed = publication is null && priorRelease?.MissingReportReviewed == true;
        var recordedReportPath = task.ResultFilePath;
        reason = await artifacts.EnsureAsync(task, roots, missingReviewed, ct);
        if (reason is not null) return await RefuseAsync(endpoint, reason, ct);
        var digest = CardDoneArtifactPreservation.Digest(task.Result);
        if (BeforeIntentAsync is not null) await BeforeIntentAsync(ct);

        // All slow inspection finished above. This lock is shared with launch admission;
        // card writers serialize through the card row. There is no Git or network I/O here.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await WorkspaceReservationJournal.LockAdmissionAsync(db, ct);
            var card = await db.Cards.FromSqlInterpolated($"SELECT * FROM \"Cards\" WHERE \"Id\" = {endpoint.Target.Cleanup.CardId} FOR UPDATE")
                .AsNoTracking().SingleAsync(ct);
            var currentGeneration = await db.CardRevisions.AsNoTracking().Where(r => r.CardId == card.Id
                    && r.ToStatus == CardStatus.Done && r.FromStatus != CardStatus.Done)
                .OrderByDescending(r => r.RevisionNumber).Select(r => r.Id).FirstOrDefaultAsync(ct);
            var fresh = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {task.Id} FOR UPDATE")
                .AsNoTracking().SingleAsync(ct);
            reason = !DoneAuthorizes(card) || currentGeneration != endpoint.Target.Cleanup.DoneRevisionId
                ? "done_generation_revoked"
                : fresh.Attempt != task.Attempt || fresh.Status != task.Status
                    || fresh.ConcurrencyToken != task.ConcurrencyToken
                    || !CoordinatesMatch(fresh, endpoint) ? "task_snapshot_changed"
                : CardDoneArtifactPreservation.Digest(fresh.Result) != digest ? "report_changed"
                : fresh.ResultFilePath != recordedReportPath || fresh.DeliverablePath != task.DeliverablePath
                    || fresh.DeliverablePdfPath != task.DeliverablePdfPath || fresh.DeliverableBundleDir != task.DeliverableBundleDir
                    ? "artifact_changed"
                : CardWorktreeCleanupService.ClassifyOwnership(fresh, endpoint.WorktreePath, endpoint.SourceFullRef, ManagedRoot());
            if (reason is null)
            {
                reason = await EligibilityAsync(fresh, endpoint, ct);
                if (reason is null && (await ConfirmedPublicationAsync(fresh, endpoint, ct))?.Id != publication?.Id)
                    reason = "publication_identity_changed";
            }
            if (reason is not null)
            {
                await tx.RollbackAsync(ct);
                return await RefuseAsync(endpoint, reason, ct);
            }
            if (priorRelease is not null)
            {
                if (priorRelease.CommandIntentId is not null)
                {
                    await tx.RollbackAsync(ct);
                    return await RefuseAsync(endpoint, "retirement_in_progress", ct);
                }
                await db.TaskWorktreeRetirements.Where(r => r.Id == priorRelease.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Active, false), ct);
            }
            var now = clock.GetUtcNow().UtcDateTime;
            var operationId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            TaskWorktreeRetirement? row = null;
            if (publication is null)
            {
                row = new TaskWorktreeRetirement
                {
                    Id = retirementId, TaskId = task.Id, TaskAttempt = task.Attempt, TerminalStatus = task.Status,
                    TaskCompletedAt = task.CompletedAt!.Value, ReportDigest = digest, MissingReportReviewed = missingReviewed,
                    ReleasedTaskRevision = task.ConcurrencyToken, CallerIdentity = "card-done",
                    ReleaseReason = "Done generation " + currentGeneration, ReleasedAt = now,
                    RepositoryPath = source.RepositoryPath, WorktreePath = source.WorktreePath,
                    CommonDirectory = snapshot.CommonDirectory, GitDirectory = snapshot.GitDirectory,
                    SourceFullRef = source.SourceFullRef, SourceSha = snapshot.HeadSha, TargetFullRef = source.TargetFullRef,
                    RemoteName = destination.RemoteName, DestinationFullRef = destination.FullRef,
                    RemoteFingerprint = destination.Fingerprint, ObservedTargetSha = observed.Sha,
                    ResultPreservationPath = task.ResultFilePath, ClaimedAt = now, ClaimAttemptId = attemptId,
                    CommandIntentId = operationId, CommandStartedAt = now, State = WorktreeRetirementState.CommandStarted,
                    UpdatedAt = now
                };
                db.TaskWorktreeRetirements.Add(row);
                db.TaskWorktreeRetirementAttempts.Add(new TaskWorktreeRetirementAttempt
                {
                    Id = attemptId, RetirementId = row.Id, SweepRunId = runId, AttemptNumber = 1,
                    CreatedAt = now, StartedAt = now, NotBefore = now, ReleasedTaskRevision = task.ConcurrencyToken,
                    CommandIntentId = operationId, CommandIntentAt = now
                });
            }
            db.WorkspaceUseReservations.Add(new WorkspaceUseReservation
            {
                Id = Guid.NewGuid(), Generation = 1, CanonicalPath = source.WorktreePath,
                SourceFullRef = source.SourceFullRef, CommonDirectory = source.RepositoryPath,
                TaskId = task.Id, RetirementId = row?.Id,
                Kind = publication is null ? WorkspaceReservationKind.Retirement : WorkspaceReservationKind.HistoricalFence,
                Active = true, CreatedAt = now
            });
            endpoint.Target.RetirementId = row?.Id;
            endpoint.Target.LandingOperationId = publication?.Id;
            endpoint.SourceSha = snapshot.HeadSha;
            endpoint.CommonDirectory = snapshot.CommonDirectory;
            endpoint.GitDirectory = snapshot.GitDirectory;
            endpoint.ReportDigest = digest;
            endpoint.OperationId = operationId;
            endpoint.RequestDigest = CardDoneArtifactPreservation.Digest(endpoint.Id + "\n" + snapshot.HeadSha + "\n" + digest);
            endpoint.IntentAt = now;
            endpoint.State = CardWorktreeCleanupEndpointState.IntentRecorded;
            endpoint.Attempts++;
            endpoint.UpdatedAt = now;
            endpoint.ConcurrencyToken = Guid.NewGuid();
            await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResultFilePath, task.ResultFilePath), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (AfterIntentAsync is not null) await AfterIntentAsync(ct);
        if (publication is not null) return await QueuePublicationAsync(endpoint, runId, ct);
        return await ExecuteAsync(endpoint, await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == retirementId, ct), runId, ct);
    }

    private async Task<string?> EligibilityAsync(AgentTask task, CardWorktreeCleanupEndpoint endpoint, CancellationToken ct)
    {
        var card = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == endpoint.Target.Cleanup.CardId, ct);
        if (!DoneAuthorizes(card)) return "done_required";
        if (task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)) return "terminal_required";
        if (task.CompletedAt is null || clock.GetUtcNow().UtcDateTime - task.CompletedAt < TimeSpan.FromMinutes(settings.Value.MinSettledMinutes)) return "settling";
        if (task.CardId != card.Id || task.Attempt != endpoint.Target.TaskAttempt || !CoordinatesMatch(task, endpoint)) return "task_snapshot_changed";
        var ownership = CardWorktreeCleanupService.ClassifyOwnership(task, endpoint.WorktreePath, endpoint.SourceFullRef, ManagedRoot());
        if (ownership is not null) return ownership;
        if (await db.AgentTaskLandRequests.AnyAsync(r => r.TaskId == task.Id && r.IsPending, ct)
            || await db.AgentTaskLandings.AnyAsync(r => r.TaskId == task.Id && r.Active, ct)
                && await ConfirmedPublicationAsync(task, endpoint, ct) is null) return "recovery_debt";
        if (await HasActiveConsumerAsync(task, endpoint, ct)) return "live_owner";
        // An ended DB row alone does not certify that a bound process is gone.
        if (task.AgentId is not null) return "process_ownership_unknown";
        var owners = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id)
            .Select(t => new { t.WorktreePath, t.WorktreeBranch }).ToListAsync(ct);
        if (owners.Any(t => WorkspaceReservationKey.PathsEqual(t.WorktreePath ?? "", endpoint.WorktreePath)
            || WorkspaceReservationKey.NormalizeRef(t.WorktreeBranch) == endpoint.SourceFullRef)) return "identity_ambiguous";
        return null;
    }

    private async Task<AgentTaskLanding?> ConfirmedPublicationAsync(AgentTask task, CardWorktreeCleanupEndpoint endpoint, CancellationToken ct)
    {
        if (task.ActiveLandingId is not Guid id || task.Status != AgentTaskStatus.Succeeded
            || task.Role == AgentTaskRole.Mutation || task.SourceLandingOperationId is not null || task.RepairSourceTaskId is not null)
            return null;
        var op = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.TaskId == task.Id, ct);
        return op is not null && op.Active && new AgentTaskLandingState().HasPublication(op)
            && op.Cleanup != LandCleanupStatus.Complete && op.ExpectedDeletionSha is not null
            && op.SourceFullRef == endpoint.SourceFullRef && op.TargetFullRef == WorkspaceReservationKey.NormalizeRef(task.MergeTargetRef ?? "master")
            && WorkspaceReservationKey.PathsEqual(op.RepositoryPath, endpoint.RepositoryPath)
            && WorkspaceReservationKey.PathsEqual(op.WorktreePath, endpoint.WorktreePath) ? op : null;
    }

    private async Task<WorktreeRemoval> QueuePublicationAsync(CardWorktreeCleanupEndpoint endpoint, Guid? runId, CancellationToken ct)
    {
        await land.RequestCardDoneCleanupRetryAsync(endpoint.Id, runId, ct);
        await db.Entry(endpoint).ReloadAsync(ct);
        return new(endpoint.RegistrationRemoved == true, endpoint.DirectoryRemoved == true, endpoint.BranchRemoved == true,
            endpoint.State == CardWorktreeCleanupEndpointState.Complete ? null : endpoint.Reason ?? "publication_cleanup_queued");
    }

    private async Task<bool> HasActiveConsumerAsync(AgentTask task, CardWorktreeCleanupEndpoint endpoint, CancellationToken ct)
    {
        if (await admission.HasLiveTaskConsumerAsync(endpoint.WorktreePath, endpoint.SourceFullRef, task.Id, ct)
            || await admission.HasLiveSessionOwnerAsync(endpoint.WorktreePath, ct)) return true;
        var key = WorkspaceReservationKey.For(endpoint.WorktreePath, endpoint.SourceFullRef, endpoint.RepositoryPath);
        var reservations = await db.WorkspaceUseReservations.AsNoTracking().Where(r => r.Active).ToListAsync(ct);
        return reservations.Any(r => WorkspaceReservationKey.Same(r.CanonicalPath, r.SourceFullRef, r.CommonDirectory, key));
    }

    private async Task<WorktreeRemoval> ExecuteAsync(CardWorktreeCleanupEndpoint endpoint, TaskWorktreeRetirement row, Guid? runId, CancellationToken ct)
    {
        var result = await retirement.TryRetireCardDoneAsync(row, endpoint.Id, runId, ct);
        if (BeforeOutcomeAsync is not null) await BeforeOutcomeAsync(ct);
        endpoint.DirectoryRemoved = result.DirectoryGone;
        endpoint.RegistrationRemoved = result.Unregistered;
        endpoint.BranchRemoved = result.BranchDeleted;
        endpoint.State = result.IsClean ? CardWorktreeCleanupEndpointState.Complete
            : result.DirectoryGone || result.Unregistered || result.BranchDeleted ? CardWorktreeCleanupEndpointState.Partial
            : CardWorktreeCleanupEndpointState.Refused;
        endpoint.Reason = result.Residue;
        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        endpoint.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<WorktreeRemoval> RefuseAsync(CardWorktreeCleanupEndpoint endpoint, string reason, CancellationToken ct)
    {
        endpoint.State = reason == "done_generation_revoked" ? CardWorktreeCleanupEndpointState.Revoked : CardWorktreeCleanupEndpointState.Refused;
        endpoint.Reason = reason;
        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        endpoint.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return new(false, false, false, reason);
    }

    private static bool CoordinatesMatch(AgentTask task, CardWorktreeCleanupEndpoint endpoint) =>
        WorkspaceReservationKey.PathsEqual(task.RepoPath ?? "", endpoint.RepositoryPath)
        && WorkspaceReservationKey.PathsEqual(task.WorktreePath ?? "", endpoint.WorktreePath)
        && WorkspaceReservationKey.NormalizeRef(task.WorktreeBranch) == endpoint.SourceFullRef;

    private string ManagedRoot() => Path.GetFullPath(gitSettings.Value.WorktreeBasePath);

    private static bool DoneAuthorizes(Card card) => card.Status == CardStatus.Done;
}

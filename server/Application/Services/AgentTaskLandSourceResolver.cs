using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0488 D-3/D-4, CARD-0688 D-2: observe the remote source against the branch ref and create the
/// schema-3 operation. Nothing here reads, fast-forwards or otherwise touches the task worktree.</summary>
public sealed class AgentTaskLandSourceResolver(
    AppDbContext db, ILandingGit git, IRepositoryMutationLease leases, TimeProvider clock,
    IOptions<GitSettings>? gitSettings = null, ILandWorkspace? landWorkspace = null)
{
    public sealed record Result(AgentTaskLanding? Operation, string? Reason, bool CreatedOperation,
        bool StaleRequest = false, LandInspectionDiagnostic? Diagnostic = null, string? Detail = null)
    {
        public static Result Stale() => new(null, null, false, StaleRequest: true);
        public static Result Refused(string reason, LandInspectionDiagnostic? diagnostic = null, string? detail = null)
            => new(null, reason, false, Diagnostic: diagnostic, Detail: detail);
    }

    private readonly AgentTaskLandRequestWriter _writer = new(db, clock);

    public async Task<Result> ResolveAsync(AgentTask task, AgentTaskLandRequest request, RepositoryLease lease,
        CancellationToken ct)
    {
        await db.Entry(request).ReloadAsync(ct);
        if (!request.IsPending || task.CurrentLandRequestId != request.Id)
            return Result.Stale();
        var baseline = LandSourceCheckpointBaseline.From(request, task);
        if (request.SchemaVersion is not 1 and not 2)
            return await RefuseAsync(task, request, baseline, "landing_schema_unsupported", null, null, null, ct);
        if (request.SchemaVersion != 2 || !GitObjectId.IsFull(request.ExpectedSourceSha))
            return await RefuseAsync(task, request, baseline, "legacy_review_binding_required", null, null, null, ct);

        if (task.RepoPath is null || task.WorktreePath is null || task.WorktreeBranch is null)
            return await RefuseAsync(task, request, baseline, "source_coordinates_missing", null, null, null, ct);
        var coordinates = new LandSourceCoordinates(task.Id, task.RepoPath, task.WorktreePath,
            FullRef(task.WorktreeBranch), FullRef(task.MergeTargetRef ?? "master"));
        if (request.SourceFullRefSnapshot is not null && request.SourceFullRefSnapshot != coordinates.SourceFullRef
            || request.TargetFullRefSnapshot is not null && request.TargetFullRefSnapshot != coordinates.TargetFullRef
            || request.RepositoryPathSnapshot is not null && !SamePath(request.RepositoryPathSnapshot, coordinates.RepositoryPath)
            || request.WorktreePathSnapshot is not null && !SamePath(request.WorktreePathSnapshot, coordinates.WorktreePath))
            return await RefuseAsync(task, request, baseline, "request_coordinates_changed", null, null, null, ct);

        var common = await git.CommonDirectoryAsync(coordinates.RepositoryPath, ct);
        if (!leases.Owns(lease, common))
            return await RefuseAsync(task, request, baseline, "repository_lease_required", null, null, null, ct);

        if (request.SourceAdvanceChildOperation is not null)
        {
            var resumableReset = request.RecoveryMode != LandRecoveryMode.None
                && request.SourceAdvanceChildOperation == "source-adopt-reset";
            if (request.SourceAdvanceChildProcessId is null || request.SourceAdvanceChildStartTicks is null)
            {
                if (request.SourceResolutionState != LandSourceResolutionState.AdvanceStarted)
                    return await RefuseAsync(task, request, baseline, "interrupted_process_requires_inspection",
                        request.LocalBeforeSha, request.RemoteSourceSha, request.ExpectedSourceSha, ct);
                if (!resumableReset) request.SourceAdvanceChildOperation = null;
            }
            else
            {
                var alive = await git.IsProcessAliveAsync(request.SourceAdvanceChildProcessId.Value,
                    request.SourceAdvanceChildStartTicks.Value, ct);
                if (alive != false)
                    return await RefuseAsync(task, request, baseline, "interrupted_process_requires_inspection",
                        request.LocalBeforeSha, request.RemoteSourceSha, request.ExpectedSourceSha, ct);
                if (!resumableReset) request.SourceAdvanceChildOperation = null;
                request.SourceAdvanceChildProcessId = null;
                request.SourceAdvanceChildStartTicks = null;
            }
            if (await CheckpointAsync(task, request, baseline, ct) is { } stop) return stop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
        }

        // CARD-0688 D-7: schema 3 never journals a source fast-forward. A request left mid-advance by the
        // old protocol re-runs -Land, once its orphaned child (if any) was proven gone above.
        if (request.SourceResolutionState == LandSourceResolutionState.AdvanceStarted
            && request.RecoveryMode == LandRecoveryMode.None)
            return await RefuseAsync(task, request, baseline, "landing_schema_superseded",
                request.LocalBeforeSha, request.RemoteSourceSha, request.ExpectedSourceSha, ct);

        if (request.RecoveryMode != LandRecoveryMode.None && request.RecoveryAdoptedAt is null)
        {
            var recovery = await RecoverSourceAsync(task, request, coordinates, baseline, ct);
            if (recovery is not null) return recovery;
            baseline = LandSourceCheckpointBaseline.From(request, task);
        }

        var expected = request.ExpectedSourceSha!;
        if (request.SourceResolutionState == LandSourceResolutionState.Resolved
            && request.ResolvedSourceSha == expected)
        {
            var existing = task.ActiveLandingId is Guid id
                ? await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == id && o.TaskId == task.Id, ct)
                : null;
            if (existing is not null && !new AgentTaskLandingState().IsTargetRaceRefusal(existing))
                return new(existing, null, false);
            return await CreateOperationAsync(task, request, coordinates, common, lease, baseline, ct);
        }

        // CARD-0688 D-2: the source is the branch ref; the task worktree is not read here.
        var branch = await LandOperationFactory.ReadBranchAsync(git, coordinates.RepositoryPath, coordinates.SourceFullRef, ct);
        if (branch.Reason is not null)
            return await RefuseAsync(task, request, baseline, branch.Reason, null, null, null, ct);
        var local = branch.Sha!;

        var prefix = $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/source-observed";
        // Authoritative observation: the fetch pins the reviewed objects locally, even for a Behind branch.
        var observed = await git.ObserveSourceAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, prefix, ct);
        if (!observed.Accepted)
            return await RefuseAsync(task, request, baseline, observed.Reason ?? "source_remote_unreadable",
                local, observed.Sha, null, ct);

        var lr = await ClassifyAsync(coordinates.RepositoryPath, local, observed.Sha!, ct);
        if (lr.Relationship is LandSourceRelationship.Unavailable or LandSourceRelationship.Unknown)
            return await RefuseAsync(task, request, baseline, lr.Reason ?? "source_remote_ancestry_error",
                local, observed.Sha, null, ct);

        var predecessorOp = task.ActiveLandingId is Guid activeId
            ? await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == activeId && o.TaskId == task.Id, ct)
            : null;
        // D-7: read-only acceptance of a branch a schema-2 rebase moved; schema 3 never creates this state.
        var derivation = LandOperationFactory.IsLegacyDerivation(predecessorOp, local, expected);

        LandSourceRelationship relationship;
        if (derivation)
        {
            var originToRemote = await ClassifyAsync(coordinates.RepositoryPath, expected, observed.Sha!, ct);
            if (originToRemote.Relationship is LandSourceRelationship.Unavailable or LandSourceRelationship.Unknown)
                return await RefuseAsync(task, request, baseline, originToRemote.Reason ?? "source_remote_ancestry_error",
                    local, observed.Sha, null, ct);
            if (originToRemote.Relationship is LandSourceRelationship.Diverged or LandSourceRelationship.Behind)
                return await RefuseObservedAsync(task, request, baseline, local, observed, originToRemote,
                    originToRemote.Relationship == LandSourceRelationship.Diverged
                        ? "source_remote_diverged" : "reviewed_source_mismatch", ct, observed.Sha);
            relationship = LandSourceRelationship.LocalAhead;
        }
        else if (lr.Relationship == LandSourceRelationship.Diverged)
            return await RefuseObservedAsync(task, request, baseline, local, observed, lr, "source_remote_diverged", ct);
        else if (lr.Relationship == LandSourceRelationship.Behind)
        {
            // CARD-0688 D-2: Behind lands from the observed remote SHA. No fast-forward of the task worktree.
            if (expected != observed.Sha)
                return await RefuseObservedAsync(task, request, baseline, local, observed, lr,
                    "reviewed_source_mismatch", ct, observed.Sha);
            relationship = LandSourceRelationship.Behind;
        }
        else
        {
            if (expected != local)
                return await RefuseObservedAsync(task, request, baseline, local, observed, lr,
                    "reviewed_source_mismatch", ct, local);
            relationship = lr.Relationship == LandSourceRelationship.Equal
                ? LandSourceRelationship.Equal : LandSourceRelationship.LocalAhead;
        }

        request.LocalBeforeSha = local;
        request.RemoteSourceSha = observed.Sha;
        request.RemoteSourceRef = coordinates.SourceFullRef;
        request.RemoteSourceFingerprint = observed.Fingerprint;
        request.SourceObservationRef = observed.ObservationRef;
        request.SourceObservedAt = clock.GetUtcNow().UtcDateTime;
        request.CandidateSourceSha = expected;
        request.SourceRelationship = relationship;
        request.SourceCommonDirectory = common;
        request.SourceWorktreePath = coordinates.WorktreePath;
        request.SourceGitDirectory = Path.Combine(common, "worktrees", Path.GetFileName(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(coordinates.WorktreePath))));
        request.SourceResolutionState = LandSourceResolutionState.Observed;
        if (await CheckpointAsync(task, request, baseline, ct) is { } observedStop) return observedStop;
        baseline = LandSourceCheckpointBaseline.From(request, task);

        // D-8: the post-checkpoint recheck is the one-round-trip ls-remote; the fetch above stays authoritative.
        var afterObserved = await RecheckRemoteAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, observed, ct);
        if (afterObserved is not null)
            return await RefuseAsync(task, request, baseline, afterObserved, local, request.RemoteSourceSha,
                expected, ct);

        request.ResolvedSourceSha = expected;
        request.SourceResolutionState = LandSourceResolutionState.Resolved;
        if (await CheckpointAsync(task, request, baseline, ct) is { } resolvedFinalStop) return resolvedFinalStop;
        baseline = LandSourceCheckpointBaseline.From(request, task);
        return await CreateOperationAsync(task, request, coordinates, common, lease, baseline, ct);
    }

    private async Task<Result> CreateOperationAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCoordinates coordinates, string common, RepositoryLease lease, LandSourceCheckpointBaseline baseline,
        CancellationToken ct)
    {
        AgentTaskLanding? previous = null;
        if (task.ActiveLandingId is Guid existingId)
        {
            previous = await db.AgentTaskLandings.SingleAsync(o => o.Id == existingId, ct);
            if (previous.Active && previous.Phase != LandPhase.Refused
                && !new AgentTaskLandingState().HasPublication(previous))
                return new(previous, null, false);
        }
        if (landWorkspace is null)
            return await RefuseAsync(task, request, baseline, "land_worktree_root_unavailable", request.LocalBeforeSha,
                request.RemoteSourceSha, request.CandidateSourceSha, ct);
        var created = await LandOperationFactory.CreateAsync(git, landWorkspace, clock.GetUtcNow().UtcDateTime,
            task, request, coordinates, common, previous, ct);
        if (created.Operation is null)
            return await RefuseAsync(task, request, baseline, created.Reason!, request.LocalBeforeSha,
                request.RemoteSourceSha, request.CandidateSourceSha, ct, created.Diagnostic, created.Detail);

        var attached = await _writer.AttachOperationAsync(task, request, baseline, created.Operation, previous, ct);
        _ = lease;
        if (attached != LandSourceCheckpointDisposition.Applied) return Result.Stale();
        return new(created.Operation, null, true);
    }

    private async Task<Result?> RecoverSourceAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCoordinates coordinates, LandSourceCheckpointBaseline baseline, CancellationToken ct)
    {
        var expected = request.ExpectedSourceSha!;
        var repository = coordinates.RepositoryPath;
        var source = request.RecoverySourceTaskId is Guid sourceId
            ? await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == sourceId, ct) : null;
        if (source is null || source.Role != AgentTaskRole.Code || source.Workspace != WorkspaceMode.Worktree
            || source.RepairSourceTaskId is not null || source.SourceLandingOperationId is not null
            || source.WorktreeBranch is null || FullRef(source.WorktreeBranch) != request.RecoverySourceFullRef
            || !LandApproval.RecoveryStatusEligible(source.Status)
            || (request.RecoveryMode == LandRecoveryMode.AdoptReviewedSource
                && (source.CardId is null || source.CardId != task.CardId || source.ProjectId != task.ProjectId))
            || (request.RecoveryMode == LandRecoveryMode.OwnerReviewedSource && source.Id != task.Id)
            || request.ReviewEvidenceId is null)
            return await RefuseAsync(task, request, baseline, "recovery_source_invalid", null, null, expected, ct);
        if (source.Id != task.Id && await db.AgentTaskLandRequests.AsNoTracking()
            .AnyAsync(r => r.TaskId == source.Id && r.IsPending, ct))
            return await RefuseAsync(task, request, baseline, "adopt_source_landing", null, null, expected, ct);
        try
        {
            await LandApproval.LoadRecoveryEvidenceAsync(db, request.ReviewEvidenceId.Value, expected, source, ct);
        }
        catch (Antiphon.Server.Application.Exceptions.ConflictException ex)
        {
            return await RefuseAsync(task, request, baseline, ex.Code ?? "recovery_review_invalid",
                null, null, expected, ct);
        }
        if (source.RepoPath is null || !SamePath(await git.CommonDirectoryAsync(source.RepoPath, ct),
                await git.CommonDirectoryAsync(repository, ct)))
            return await RefuseAsync(task, request, baseline, "adopt_source_foreign_repository",
                null, null, expected, ct);

        var sourceObserved = await git.ObserveSourceAsync(repository, request.RecoverySourceFullRef!,
            $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/recover-source-observed", ct);
        if (!sourceObserved.Accepted || sourceObserved.Sha != expected)
            return await RefuseAsync(task, request, baseline,
                sourceObserved.Reason ?? "recovery_source_not_remote_tip", null, sourceObserved.Sha, expected, ct);
        if (request.RecoverySourceFingerprint is not null
            && request.RecoverySourceFingerprint != sourceObserved.Fingerprint)
            return await RefuseAsync(task, request, baseline, "source_remote_endpoint_changed",
                null, sourceObserved.Sha, expected, ct);
        var ownerObserved = request.RecoveryMode == LandRecoveryMode.OwnerReviewedSource
            ? sourceObserved
            : await git.ObserveSourceAsync(repository, coordinates.SourceFullRef,
                $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/recover-owner-observed", ct);
        if (!ownerObserved.Accepted && ownerObserved.Reason != "source_remote_missing")
            return await RefuseAsync(task, request, baseline,
                ownerObserved.Reason ?? "source_remote_unreadable", null, ownerObserved.Sha, expected, ct);
        if (ownerObserved.Fingerprint != sourceObserved.Fingerprint)
            return await RefuseAsync(task, request, baseline, "source_remote_endpoint_changed",
                null, ownerObserved.Sha, expected, ct);
        if (request.RecoveryOwnerRemoteBeforeSha is not null
            && ownerObserved.Sha != request.RecoveryOwnerRemoteBeforeSha && ownerObserved.Sha != expected)
            return await RefuseAsync(task, request, baseline, "adopt_source_remote_changed",
                null, ownerObserved.Sha, expected, ct);

        async Task StartedAsync(int pid, long ticks, CancellationToken startedCt)
        {
            request.SourceAdvanceChildProcessId = pid;
            request.SourceAdvanceChildStartTicks = ticks;
            request.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(startedCt);
        }
        var inspected = await git.InspectAsync(coordinates, LandInspectionScope.IdentityAndStatus, ct);
        if (!inspected.Accepted && inspected.Reason == "source_dirty"
            && request.SourceAdvanceChildOperation == "source-adopt-reset"
            && GitObjectId.IsFull(request.RecoveryLocalBeforeSha))
        {
            // An interrupted update-ref leaves HEAD at S while the index and worktree still show L.
            // Reset only when the checkout still equals the pinned old L; actual new edits refuse.
            var identity = await git.InspectAsync(coordinates, LandInspectionScope.IdentityOnly, ct);
            if (!identity.Accepted || identity.Snapshot!.HeadSha != expected)
                return await RefuseAsync(task, request, baseline, "adopt_local_changed",
                    identity.Snapshot?.HeadSha, ownerObserved.Sha, expected, ct);
            var unchanged = await git.RunAsync(coordinates.WorktreePath,
                ["diff", "--quiet", request.RecoveryLocalBeforeSha!], ct);
            var untracked = await git.RunAsync(coordinates.WorktreePath,
                ["ls-files", "--others", "--exclude-standard"], ct);
            if (!unchanged.Succeeded || !untracked.Succeeded || untracked.Output.Length != 0)
                return await RefuseAsync(task, request, baseline, "source_dirty",
                    expected, ownerObserved.Sha, expected, ct);
            var resetResume = await git.RunOwnedAsync(coordinates.WorktreePath,
                ["reset", "--hard", expected], StartedAsync, ct);
            baseline = LandSourceCheckpointBaseline.From(request, task);
            if (!resetResume.Succeeded)
                return await RefuseAsync(task, request, baseline, "adopt_local_reset_failed",
                    expected, ownerObserved.Sha, expected, ct);
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            inspected = await git.InspectAsync(coordinates, LandInspectionScope.IdentityAndStatus, ct);
        }
        if (!inspected.Accepted)
            return await RefuseAsync(task, request, baseline, inspected.Reason ?? "source_identity_unreadable",
                null, ownerObserved.Sha, expected, ct, inspected.Diagnostic);
        var local = inspected.Snapshot!.HeadSha;
        if (request.RecoveryLocalBeforeSha is not null && local != request.RecoveryLocalBeforeSha && local != expected)
            return await RefuseAsync(task, request, baseline, "adopt_local_cas_changed",
                local, ownerObserved.Sha, expected, ct);

        if (request.RecoveryMode == LandRecoveryMode.AdoptReviewedSource)
        {
            if (!GitObjectId.IsFull(source.WorktreeBaseSha))
                return await RefuseAsync(task, request, baseline, "adopt_source_lineage", local,
                    ownerObserved.Sha, expected, ct);
            var candidates = new List<string>();
            if (GitObjectId.IsFull(ownerObserved.Sha)) candidates.Add(ownerObserved.Sha!);
            if (GitObjectId.IsFull(request.RecoveryOwnerRemoteBeforeSha))
                candidates.Add(request.RecoveryOwnerRemoteBeforeSha!);
            candidates.Add(local);
            if (GitObjectId.IsFull(request.RecoveryLocalBeforeSha))
                candidates.Add(request.RecoveryLocalBeforeSha!);
            if (request.SupersedesRequestId is Guid oldId)
            {
                var old = await db.AgentTaskLandRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == oldId, ct);
                if (old is not null && GitObjectId.IsFull(old.ExpectedSourceSha))
                    candidates.Add(old.ExpectedSourceSha!);
            }
            var lineage = false;
            foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                if (candidate == source.WorktreeBaseSha) { lineage = true; break; }
                var ancestor = await git.RunAsync(repository,
                    ["merge-base", "--is-ancestor", candidate, source.WorktreeBaseSha!], ct);
                if (ancestor.ExitCode == 0) { lineage = true; break; }
                if (ancestor.ExitCode != 1)
                    return await RefuseAsync(task, request, baseline, "adopt_source_lineage_unreadable",
                        local, ownerObserved.Sha, expected, ct);
            }
            if (!lineage)
                return await RefuseAsync(task, request, baseline, "adopt_source_lineage",
                    local, ownerObserved.Sha, expected, ct);
        }

        var beforeRemote = request.RecoveryOwnerRemoteBeforeSha ?? ownerObserved.Sha;
        if (request.RecoveryMode == LandRecoveryMode.AdoptReviewedSource && beforeRemote is not null)
        {
            var patches = await git.RunAsync(repository, ["cherry", expected, beforeRemote], ct);
            if (!patches.Succeeded)
                return await RefuseAsync(task, request, baseline, "adopt_source_patch_evidence_unreadable",
                    local, beforeRemote, expected, ct);
            var uncontained = patches.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("+ ", StringComparison.Ordinal))
                .Select(line => line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
                .ToArray();
            request.RecoveryPatchesContained = uncontained.Length == 0;
            request.RecoveryUncontainedPatches = string.Join(",", uncontained);
        }
        var pinPrefix = $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/adopt";
        foreach (var (name, sha) in new[] { ("local-before", request.RecoveryLocalBeforeSha ?? local), ("source", expected),
                     ("remote-before", beforeRemote) })
        {
            if (sha is null) continue;
            var pin = await git.PinAsync(repository, $"{pinPrefix}/{name}", sha, ct);
            if (!pin.Succeeded)
                return await RefuseAsync(task, request, baseline, "adopt_source_pin_failed",
                    local, ownerObserved.Sha, expected, ct);
        }
        request.RecoverySourceFingerprint ??= sourceObserved.Fingerprint;
        request.RecoveryLocalBeforeSha ??= local;
        request.RecoveryOwnerRemoteBeforeSha ??= beforeRemote;
        request.SourceResolutionState = LandSourceResolutionState.AdvanceStarted;
        if (await CheckpointAsync(task, request, baseline, ct) is { } stale) return stale;
        baseline = LandSourceCheckpointBaseline.From(request, task);
        if (local != expected)
        {
            var authority = await RecheckRecoveryAuthorityAsync(request, source, sourceObserved, ct);
            if (authority is not null)
                return await RefuseAsync(task, request, baseline, authority, local, ownerObserved.Sha, expected, ct);
            if (ownerObserved.Sha is not null)
            {
                var recheck = await git.RecheckSourceRemoteAsync(repository, coordinates.SourceFullRef,
                    ownerObserved.Sha, ownerObserved.Fingerprint!, ct);
                if (!recheck.Accepted || recheck.Sha != ownerObserved.Sha)
                    return await RefuseAsync(task, request, baseline, "adopt_source_remote_changed",
                        local, recheck.Sha, expected, ct);
            }
            request.SourceAdvanceChildOperation = "source-adopt-reset";
            if (await CheckpointAsync(task, request, baseline, ct) is { } resetStop) return resetStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
            var moved = await git.RunOwnedAsync(repository,
                ["update-ref", "--no-deref", coordinates.SourceFullRef, expected, local], StartedAsync, ct);
            baseline = LandSourceCheckpointBaseline.From(request, task);
            if (!moved.Succeeded)
                return await RefuseAsync(task, request, baseline, "adopt_local_cas_rejected",
                    local, ownerObserved.Sha, expected, ct);
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            request.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            var reset = await git.RunOwnedAsync(coordinates.WorktreePath,
                ["reset", "--hard", expected], StartedAsync, ct);
            baseline = LandSourceCheckpointBaseline.From(request, task);
            if (!reset.Succeeded)
                return await RefuseAsync(task, request, baseline, "adopt_local_reset_failed",
                    local, ownerObserved.Sha, expected, ct);
            request.SourceAdvanceChildOperation = null;
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            if (await CheckpointAsync(task, request, baseline, ct) is { } advancedStop) return advancedStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
            var afterLocal = await git.InspectAsync(coordinates, LandInspectionScope.IdentityAndStatus, ct);
            if (!afterLocal.Accepted || afterLocal.Snapshot!.HeadSha != expected)
                return await RefuseAsync(task, request, baseline, "adopt_local_changed",
                    afterLocal.Snapshot?.HeadSha, ownerObserved.Sha, expected, ct);
        }
        if (request.SourceAdvanceChildOperation == "source-adopt-reset")
        {
            request.SourceAdvanceChildOperation = null;
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            if (await CheckpointAsync(task, request, baseline, ct) is { } resetFinalStop) return resetFinalStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
        }

        if (ownerObserved.Sha != expected)
        {
            var authority = await RecheckRecoveryAuthorityAsync(request, source, sourceObserved, ct);
            if (authority is not null)
                return await RefuseAsync(task, request, baseline, authority, local, ownerObserved.Sha, expected, ct);
            if (ownerObserved.Sha is not null)
            {
                var recheck = await git.RecheckSourceRemoteAsync(repository, coordinates.SourceFullRef,
                    ownerObserved.Sha, ownerObserved.Fingerprint!, ct);
                if (!recheck.Accepted || recheck.Sha != ownerObserved.Sha)
                    return await RefuseAsync(task, request, baseline, "adopt_source_remote_changed",
                        local, recheck.Sha, expected, ct);
            }
            request.SourceAdvanceChildOperation = "source-adopt-push";
            if (await CheckpointAsync(task, request, baseline, ct) is { } pushStop) return pushStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
            var pushed = await git.PushSourceOwnedAsync(repository, coordinates.SourceFullRef, expected,
                ownerObserved.Sha, ownerObserved.Fingerprint!, StartedAsync, ct);
            baseline = LandSourceCheckpointBaseline.From(request, task);
            if (!pushed.Succeeded)
                return await RefuseAsync(task, request, baseline, "adopt_source_push_rejected",
                    local, ownerObserved.Sha, expected, ct);
            request.SourceAdvanceChildOperation = null;
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            if (await CheckpointAsync(task, request, baseline, ct) is { } pushedStop) return pushedStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
        }
        var finalRemote = await git.RecheckSourceRemoteAsync(repository, coordinates.SourceFullRef,
            expected, ownerObserved.Fingerprint!, ct);
        if (!finalRemote.Accepted || finalRemote.Sha != expected)
            return await RefuseAsync(task, request, baseline, "adopt_source_push_unconfirmed",
                local, finalRemote.Sha, expected, ct);
        if (request.RecoveryMode == LandRecoveryMode.OwnerReviewedSource)
            request.RecoveryRelationship = "OwnerRemote";
        else if (beforeRemote is null)
            request.RecoveryRelationship = "NewOwnerRef";
        else
        {
            var ancestry = await git.RunAsync(repository,
                ["merge-base", "--is-ancestor", beforeRemote, expected], ct);
            if (ancestry.ExitCode is not (0 or 1))
                return await RefuseAsync(task, request, baseline, "adopt_source_ancestry_unreadable",
                    local, finalRemote.Sha, expected, ct);
            request.RecoveryRelationship = ancestry.ExitCode == 0 ? "Descendant" : "Rewritten";
        }
        request.RecoveryOwnerRemoteAfterSha = expected;
        request.RecoveryAdoptedAt = clock.GetUtcNow().UtcDateTime;
        request.SourceResolutionState = LandSourceResolutionState.None;
        if (await CheckpointAsync(task, request, baseline, ct) is { } finalStop) return finalStop;
        return null;
    }

    private async Task<string?> RecheckRecoveryAuthorityAsync(AgentTaskLandRequest request, AgentTask source,
        LandingSourceObservation observed, CancellationToken ct)
    {
        try
        {
            await LandApproval.LoadRecoveryEvidenceAsync(db, request.ReviewEvidenceId!.Value,
                request.ExpectedSourceSha!, source, ct);
        }
        catch (Antiphon.Server.Application.Exceptions.ConflictException ex)
        {
            return ex.Code ?? "recovery_review_invalid";
        }
        var remote = await git.RecheckSourceRemoteAsync(request.RepositoryPathSnapshot!,
            request.RecoverySourceFullRef!, request.ExpectedSourceSha!, observed.Fingerprint!, ct);
        return remote.Accepted && remote.Sha == request.ExpectedSourceSha
            ? null : remote.Reason ?? "recovery_source_changed";
    }

    private async Task<LandingSourceGraph> ClassifyAsync(string repository, string local, string remote, CancellationToken ct)
    {
        if (local == remote) return new(LandSourceRelationship.Equal, null);
        var localIsAncestor = await git.RunAsync(repository, ["merge-base", "--is-ancestor", local, remote], ct);
        if (localIsAncestor.ExitCode is not 0 and not 1)
            return new(LandSourceRelationship.Unavailable, "source_remote_ancestry_error");
        var remoteIsAncestor = await git.RunAsync(repository, ["merge-base", "--is-ancestor", remote, local], ct);
        if (remoteIsAncestor.ExitCode is not 0 and not 1)
            return new(LandSourceRelationship.Unavailable, "source_remote_ancestry_error");
        if (localIsAncestor.ExitCode == 0) return new(LandSourceRelationship.Behind, null);
        if (remoteIsAncestor.ExitCode == 0) return new(LandSourceRelationship.LocalAhead, null);
        return new(LandSourceRelationship.Diverged, "source_remote_diverged");
    }

    private async Task<Result> RefuseObservedAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, string local, LandingSourceObservation observed,
        LandingSourceGraph graph, string reason, CancellationToken ct, string? candidate = null)
    {
        request.LocalBeforeSha = local;
        request.RemoteSourceSha = observed.Sha;
        request.RemoteSourceFingerprint = observed.Fingerprint;
        request.SourceObservationRef = observed.ObservationRef;
        request.SourceObservedAt = clock.GetUtcNow().UtcDateTime;
        request.SourceRelationship = graph.Relationship;
        request.CandidateSourceSha = candidate;
        request.SourceRefusalReason = reason;
        return await PersistRefusalAsync(task, request, baseline, reason, ct);
    }

    private async Task<Result> RefuseAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, string reason, string? local, string? remote,
        string? candidate, CancellationToken ct, LandInspectionDiagnostic? diagnostic = null, string? detail = null)
    {
        request.SourceRefusalReason = reason;
        request.LocalBeforeSha = local;
        request.RemoteSourceSha = remote;
        request.CandidateSourceSha = candidate;
        if (diagnostic is not null) LandFailureDiagnostic.ApplyInspection(request, diagnostic);
        return await PersistRefusalAsync(task, request, baseline, reason, ct, diagnostic, detail);
    }

    private async Task<Result> PersistRefusalAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, string reason, CancellationToken ct,
        LandInspectionDiagnostic? diagnostic = null, string? detail = null)
    {
        var applied = await _writer.ApplyAsync(task, request, baseline, LandSourceCheckpointPatch.From(request), ct);
        if (applied.Disposition == LandSourceCheckpointDisposition.StaleRequest) return Result.Stale();
        if (applied.Disposition == LandSourceCheckpointDisposition.SourceStateChanged)
            throw new LandSourceResolutionConflictException();
        return Result.Refused(reason, diagnostic, detail);
    }

    private async Task<Result?> CheckpointAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, CancellationToken ct)
    {
        var applied = await _writer.ApplyAsync(task, request, baseline, LandSourceCheckpointPatch.From(request), ct);
        if (applied.Disposition == LandSourceCheckpointDisposition.Applied) return null;
        if (applied.Disposition == LandSourceCheckpointDisposition.StaleRequest) return Result.Stale();
        throw new LandSourceResolutionConflictException();
    }

    private async Task<string?> RecheckRemoteAsync(string repository, string sourceFullRef,
        LandingSourceObservation accepted, CancellationToken ct)
    {
        var recheck = await git.RecheckSourceRemoteAsync(repository, sourceFullRef, accepted.Sha!, accepted.Fingerprint!, ct);
        if (!recheck.Accepted) return recheck.Reason ?? "source_remote_unreadable";
        return recheck.Sha == accepted.Sha && recheck.Fingerprint == accepted.Fingerprint ? null : "source_remote_changed";
    }

    private static string FullRef(string branch) =>
        branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

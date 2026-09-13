using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0488 D-3/D-4: observe remote source and journal a behind-only fast-forward before preparation.</summary>
public sealed class AgentTaskLandSourceResolver(
    AppDbContext db, ILandingGit git, IRepositoryMutationLease leases, TimeProvider clock)
{
    public sealed record Result(AgentTaskLanding? Operation, string? Reason, bool CreatedOperation,
        bool StaleRequest = false, LandInspectionDiagnostic? Diagnostic = null)
    {
        public static Result Stale() => new(null, null, false, StaleRequest: true);
        public static Result Refused(string reason, LandInspectionDiagnostic? diagnostic = null)
            => new(null, reason, false, Diagnostic: diagnostic);
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
            if (request.SourceAdvanceChildProcessId is null || request.SourceAdvanceChildStartTicks is null)
            {
                if (request.SourceResolutionState != LandSourceResolutionState.AdvanceStarted)
                    return await RefuseAsync(task, request, baseline, "interrupted_process_requires_inspection",
                        request.LocalBeforeSha, request.RemoteSourceSha, request.ExpectedSourceSha, ct);
                request.SourceAdvanceChildOperation = null;
            }
            else
            {
                var alive = await git.IsProcessAliveAsync(request.SourceAdvanceChildProcessId.Value,
                    request.SourceAdvanceChildStartTicks.Value, ct);
                if (alive != false)
                    return await RefuseAsync(task, request, baseline, "interrupted_process_requires_inspection",
                        request.LocalBeforeSha, request.RemoteSourceSha, request.ExpectedSourceSha, ct);
                request.SourceAdvanceChildOperation = null;
                request.SourceAdvanceChildProcessId = null;
                request.SourceAdvanceChildStartTicks = null;
            }
            if (await CheckpointAsync(task, request, baseline, ct) is { } stop) return stop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
        }

        var expected = request.ExpectedSourceSha!;
        if (request.SourceResolutionState == LandSourceResolutionState.Resolved
            && request.ResolvedSourceSha == expected)
        {
            var existing = task.ActiveLandingId is Guid id
                ? await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == id && o.TaskId == task.Id, ct)
                : null;
            if (existing is not null) return new(existing, null, false);
            return await CreateOperationAsync(task, request, coordinates, lease, baseline, ct);
        }

        var inspection = await git.InspectAsync(coordinates, ct);
        if (!inspection.Accepted)
            return await RefuseAsync(task, request, baseline, inspection.Reason ?? "source_unknown",
                inspection.Snapshot?.HeadSha, null, null, ct, inspection.Diagnostic);
        var local = inspection.Snapshot!;
        if (request.SourceResolutionState == LandSourceResolutionState.AdvanceStarted)
        {
            if (local.HeadSha == expected)
            {
                request.SourceResolutionState = LandSourceResolutionState.Resolved;
                request.ResolvedSourceSha = expected;
                if (await CheckpointAsync(task, request, baseline, ct) is { } resolvedStop) return resolvedStop;
                baseline = LandSourceCheckpointBaseline.From(request, task);
                return await CreateOperationAsync(task, request, coordinates, lease, baseline, ct);
            }

            if (request.LocalBeforeSha is { } savedL && local.HeadSha == savedL && request.ResolvedSourceSha is null
                && request.RemoteSourceSha is { } savedR && request.RemoteSourceFingerprint is { Length: 64 })
            {
                var retryPrefix = $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/source-observed";
                var moved = await RecheckRemoteAsync(local.RegisteredPath, coordinates.SourceFullRef, retryPrefix,
                    new(savedR, request.SourceObservationRef ?? retryPrefix, request.RemoteSourceFingerprint, null), ct);
                if (moved is not null)
                    return await RefuseAsync(task, request, baseline, moved, local.HeadSha, savedR, expected, ct);
                request.SourceAdvanceChildOperation = "source-ff";
                if (await CheckpointAsync(task, request, baseline, ct) is { } retryAdvanceStop) return retryAdvanceStop;
                baseline = LandSourceCheckpointBaseline.From(request, task);
                var merge = await git.RunOwnedAsync(local.RegisteredPath,
                    ["-c", "merge.autoStash=false", "merge", "--ff-only", expected],
                    async (pid, ticks, token) =>
                    {
                        request.SourceAdvanceChildProcessId = pid;
                        request.SourceAdvanceChildStartTicks = ticks;
                        await CheckpointCallbackAsync(task, request, baseline, token);
                        baseline = LandSourceCheckpointBaseline.From(request, task);
                    }, ct);
                request.SourceAdvanceChildOperation = null;
                request.SourceAdvanceChildProcessId = null;
                request.SourceAdvanceChildStartTicks = null;
                if (await CheckpointAsync(task, request, baseline, ct) is { } retryClearStop) return retryClearStop;
                baseline = LandSourceCheckpointBaseline.From(request, task);
                if (!merge.Succeeded)
                    return await RefuseAsync(task, request, baseline, "source_fast_forward_failed", local.HeadSha, savedR, expected, ct);
                var after = await git.InspectAsync(coordinates, ct);
                if (!after.Accepted || after.Snapshot!.HeadSha != expected)
                    return await RefuseAsync(task, request, baseline, after.Reason ?? "source_changed",
                        after.Snapshot?.HeadSha, savedR, expected, ct, after.Diagnostic);
                request.ResolvedSourceSha = expected;
                request.SourceResolutionState = LandSourceResolutionState.Resolved;
                if (await CheckpointAsync(task, request, baseline, ct) is { } retryResolvedStop) return retryResolvedStop;
                baseline = LandSourceCheckpointBaseline.From(request, task);
                return await CreateOperationAsync(task, request, coordinates, lease, baseline, ct);
            }

            return await RefuseAsync(task, request, baseline, "source_advance_head_unexpected", local.HeadSha,
                request.RemoteSourceSha, expected, ct);
        }

        if (request.SourceResolutionState == LandSourceResolutionState.Observed
            && local.HeadSha == expected
            && request.LocalBeforeSha is { } recorded && recorded != expected)
            return await RefuseAsync(task, request, baseline, "source_advance_head_unexpected", local.HeadSha,
                request.RemoteSourceSha, expected, ct);

        var prefix = $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/source-observed";
        var observed = await git.ObserveSourceAsync(local.RegisteredPath, coordinates.SourceFullRef, prefix, ct);
        if (!observed.Accepted)
            return await RefuseAsync(task, request, baseline, observed.Reason ?? "source_remote_unreadable",
                local.HeadSha, observed.Sha, null, ct);

        var lr = await ClassifyAsync(local.RegisteredPath, local.HeadSha, observed.Sha!, ct);
        if (lr.Relationship is LandSourceRelationship.Unavailable or LandSourceRelationship.Unknown)
            return await RefuseAsync(task, request, baseline, lr.Reason ?? "source_remote_ancestry_error",
                local.HeadSha, observed.Sha, null, ct);

        var predecessorOp = task.ActiveLandingId is Guid activeId
            ? await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == activeId && o.TaskId == task.Id, ct)
            : null;
        var derivation = predecessorOp is { RebasedSourceSha: { } prepared } && prepared == local.HeadSha
            && predecessorOp.OriginalSourceSha == expected;

        bool needFf;
        LandSourceRelationship relationship;
        if (derivation)
        {
            var originToRemote = await ClassifyAsync(local.RegisteredPath, expected, observed.Sha!, ct);
            if (originToRemote.Relationship is LandSourceRelationship.Unavailable or LandSourceRelationship.Unknown)
                return await RefuseAsync(task, request, baseline, originToRemote.Reason ?? "source_remote_ancestry_error",
                    local.HeadSha, observed.Sha, null, ct);
            if (originToRemote.Relationship is LandSourceRelationship.Diverged or LandSourceRelationship.Behind)
                return await RefuseObservedAsync(task, request, baseline, local, observed, originToRemote,
                    originToRemote.Relationship == LandSourceRelationship.Diverged
                        ? "source_remote_diverged" : "reviewed_source_mismatch", ct, observed.Sha);
            needFf = false;
            relationship = LandSourceRelationship.LocalAhead;
        }
        else if (lr.Relationship == LandSourceRelationship.Diverged)
            return await RefuseObservedAsync(task, request, baseline, local, observed, lr, "source_remote_diverged", ct);
        else if (lr.Relationship == LandSourceRelationship.Behind)
        {
            if (expected != observed.Sha)
                return await RefuseObservedAsync(task, request, baseline, local, observed, lr,
                    "reviewed_source_mismatch", ct, observed.Sha);
            needFf = true;
            relationship = LandSourceRelationship.Behind;
        }
        else
        {
            if (expected != local.HeadSha)
                return await RefuseObservedAsync(task, request, baseline, local, observed, lr,
                    "reviewed_source_mismatch", ct, local.HeadSha);
            needFf = false;
            relationship = lr.Relationship == LandSourceRelationship.Equal
                ? LandSourceRelationship.Equal : LandSourceRelationship.LocalAhead;
        }

        var candidate = expected;

        request.LocalBeforeSha = local.HeadSha;
        request.RemoteSourceSha = observed.Sha;
        request.RemoteSourceRef = coordinates.SourceFullRef;
        request.RemoteSourceFingerprint = observed.Fingerprint;
        request.SourceObservationRef = observed.ObservationRef;
        request.SourceObservedAt = clock.GetUtcNow().UtcDateTime;
        request.CandidateSourceSha = candidate;
        request.SourceRelationship = relationship;
        request.SourceCommonDirectory = local.CommonDirectory;
        request.SourceWorktreePath = local.RegisteredPath;
        request.SourceGitDirectory = local.GitDirectory;
        request.SourceResolutionState = LandSourceResolutionState.Observed;
        if (await CheckpointAsync(task, request, baseline, ct) is { } observedStop) return observedStop;
        baseline = LandSourceCheckpointBaseline.From(request, task);

        var afterObserved = await RecheckRemoteAsync(local.RegisteredPath, coordinates.SourceFullRef, prefix,
            observed, ct);
        if (afterObserved is not null)
            return await RefuseAsync(task, request, baseline, afterObserved, local.HeadSha, request.RemoteSourceSha,
                expected, ct);

        if (needFf)
        {
            var still = await git.InspectAsync(coordinates, ct);
            if (!still.Accepted || still.Snapshot!.HeadSha != local.HeadSha
                || still.Snapshot.GitDirectory != local.GitDirectory
                || still.Snapshot.CommonDirectory != local.CommonDirectory)
                return await RefuseAsync(task, request, baseline, still.Reason ?? "source_changed",
                    still.Snapshot?.HeadSha, observed.Sha, expected, ct, still.Diagnostic);

            request.SourceResolutionState = LandSourceResolutionState.AdvanceStarted;
            request.SourceAdvanceChildOperation = "source-ff";
            if (await CheckpointAsync(task, request, baseline, ct) is { } advanceStop) return advanceStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
            var beforeMerge = await RecheckRemoteAsync(local.RegisteredPath, coordinates.SourceFullRef, prefix,
                observed, ct);
            if (beforeMerge is not null)
                return await RefuseAsync(task, request, baseline, beforeMerge, local.HeadSha, request.RemoteSourceSha,
                    expected, ct);
            var merge = await git.RunOwnedAsync(local.RegisteredPath,
                ["-c", "merge.autoStash=false", "merge", "--ff-only", expected],
                async (pid, ticks, token) =>
                {
                    request.SourceAdvanceChildProcessId = pid;
                    request.SourceAdvanceChildStartTicks = ticks;
                    await CheckpointCallbackAsync(task, request, baseline, token);
                    baseline = LandSourceCheckpointBaseline.From(request, task);
                }, ct);
            request.SourceAdvanceChildOperation = null;
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            if (await CheckpointAsync(task, request, baseline, ct) is { } clearStop) return clearStop;
            baseline = LandSourceCheckpointBaseline.From(request, task);
            if (!merge.Succeeded)
                return await RefuseAsync(task, request, baseline, "source_fast_forward_failed", local.HeadSha,
                    observed.Sha, expected, ct);
            var after = await git.InspectAsync(coordinates, ct);
            if (!after.Accepted || after.Snapshot!.HeadSha != expected
                || after.Snapshot.GitDirectory != local.GitDirectory
                || after.Snapshot.CommonDirectory != local.CommonDirectory
                || after.Snapshot.RegisteredPath != local.RegisteredPath)
                return await RefuseAsync(task, request, baseline, after.Reason ?? "source_changed",
                    after.Snapshot?.HeadSha, observed.Sha, expected, ct, after.Diagnostic);
            var afterFf = await RecheckRemoteAsync(local.RegisteredPath, coordinates.SourceFullRef, prefix,
                observed, ct);
            if (afterFf is not null)
                return await RefuseAsync(task, request, baseline, afterFf, expected, request.RemoteSourceSha,
                    expected, ct);
        }

        request.ResolvedSourceSha = expected;
        request.SourceResolutionState = LandSourceResolutionState.Resolved;
        if (await CheckpointAsync(task, request, baseline, ct) is { } resolvedFinalStop) return resolvedFinalStop;
        baseline = LandSourceCheckpointBaseline.From(request, task);
        return await CreateOperationAsync(task, request, coordinates, lease, baseline, ct);
    }

    private async Task<Result> CreateOperationAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCoordinates coordinates, RepositoryLease lease, LandSourceCheckpointBaseline baseline,
        CancellationToken ct)
    {
        if (task.ActiveLandingId is Guid existingId)
        {
            var existing = await db.AgentTaskLandings.SingleAsync(o => o.Id == existingId, ct);
            if (existing.Active && existing.Phase != LandPhase.Refused
                && !new AgentTaskLandingState().HasPublication(existing))
                return new(existing, null, false);
        }

        LandingDestination destination;
        try { destination = await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct); }
        catch (IOException ex)
        {
            return await RefuseAsync(task, request, baseline, "landing_io_error", request.LocalBeforeSha,
                request.RemoteSourceSha, request.CandidateSourceSha, ct, LandFailureDiagnostic.FromIo(ex));
        }
        string target;
        LandingGitResult? commitLookup = null;
        try
        {
            commitLookup = await git.RunAsync(coordinates.RepositoryPath,
                ["rev-parse", "--verify", coordinates.TargetFullRef + "^{commit}"], ct);
            target = commitLookup.Output.Trim();
            if (!commitLookup.Succeeded || !GitObjectId.IsFull(target))
                throw new InvalidOperationException("commit_lookup_failed");
        }
        catch (InvalidOperationException)
        {
            var diagnostic = commitLookup is null
                ? null
                : LandFailureDiagnostic.FromCommand(["rev-parse", "--verify"], commitLookup.ExitCode);
            return await RefuseAsync(task, request, baseline, "commit_lookup_failed", request.LocalBeforeSha,
                request.RemoteSourceSha, request.CandidateSourceSha, ct, diagnostic);
        }
        var fresh = await git.InspectAsync(coordinates, ct);
        if (!fresh.Accepted)
            return await RefuseAsync(task, request, baseline, fresh.Reason ?? "source_unknown",
                fresh.Snapshot?.HeadSha, request.RemoteSourceSha, request.CandidateSourceSha, ct, fresh.Diagnostic);
        var snapshot = fresh.Snapshot!;
        AgentTaskLanding? previous = null;
        if (task.ActiveLandingId is Guid previousId)
            previous = await db.AgentTaskLandings.SingleAsync(o => o.Id == previousId, ct);
        var derivation = previous is { RebasedSourceSha: { } prepared }
            && prepared == snapshot.HeadSha && prepared != request.ExpectedSourceSha;
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = task.Id, SchemaVersion = 2, Phase = LandPhase.Inspected,
            CreatedAt = Now(), UpdatedAt = Now(), RepositoryPath = coordinates.RepositoryPath,
            WorktreePath = snapshot.RegisteredPath, CommonDirectory = snapshot.CommonDirectory,
            GitDirectory = snapshot.GitDirectory, SourceFullRef = coordinates.SourceFullRef,
            OriginalSourceSha = request.ExpectedSourceSha!, ReviewedSourceSha = request.ExpectedSourceSha,
            PreparationInputSha = derivation ? previous!.RebasedSourceSha : request.ExpectedSourceSha,
            PreviousPreparationOperationId = derivation ? previous!.Id : null,
            ApprovalLandRequestId = previous is { OriginalSourceSha: { } prev } && prev == request.ExpectedSourceSha
                ? previous.ApprovalLandRequestId ?? request.Id : request.Id,
            ReviewEvidenceId = previous?.ReviewEvidenceId ?? request.ReviewEvidenceId,
            ApprovalKind = request.ApprovalKind,
            ApprovedAt = request.ApprovedAt ?? request.RequestedAt,
            SourceRemoteSha = request.RemoteSourceSha, SourceRemoteRef = request.RemoteSourceRef,
            SourceRemoteFingerprint = request.RemoteSourceFingerprint,
            SourceRemoteObservedAt = request.SourceObservedAt,
            TargetFullRef = coordinates.TargetFullRef, TargetBeforeSha = target,
            RemoteName = destination.RemoteName, DestinationFullRef = destination.FullRef,
            RemoteFingerprint = destination.Fingerprint, VerificationFilter = request.VerifyFilter ?? task.LandVerifyFilter,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{task.Id:N}/{op.Id:N}";
        try
        {
            op.TargetCheckoutPath = await TargetCheckoutAsync(op, ct);
            op.TargetCheckoutRecorded = true;
        }
        catch (InvalidOperationException ex)
        {
            return await RefuseAsync(task, request, baseline, ex.Message, request.LocalBeforeSha,
                request.RemoteSourceSha, request.CandidateSourceSha, ct);
        }

        var attached = await _writer.AttachOperationAsync(task, request, baseline, op, previous, ct);
        _ = lease;
        if (attached == LandSourceCheckpointDisposition.StaleRequest) return Result.Stale();
        if (attached != LandSourceCheckpointDisposition.Applied) return Result.Stale();
        return new(op, null, true);
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
        LandSourceCheckpointBaseline baseline, LandSourceSnapshot local, LandingSourceObservation observed,
        LandingSourceGraph graph, string reason, CancellationToken ct, string? candidate = null)
    {
        request.LocalBeforeSha = local.HeadSha;
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
        string? candidate, CancellationToken ct, LandInspectionDiagnostic? diagnostic = null)
    {
        request.SourceRefusalReason = reason;
        request.LocalBeforeSha = local;
        request.RemoteSourceSha = remote;
        request.CandidateSourceSha = candidate;
        if (diagnostic is not null) LandFailureDiagnostic.ApplyInspection(request, diagnostic);
        return await PersistRefusalAsync(task, request, baseline, reason, ct, diagnostic);
    }

    private async Task<Result> PersistRefusalAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, string reason, CancellationToken ct,
        LandInspectionDiagnostic? diagnostic = null)
    {
        var applied = await _writer.ApplyAsync(task, request, baseline, LandSourceCheckpointPatch.From(request), ct);
        if (applied.Disposition == LandSourceCheckpointDisposition.StaleRequest) return Result.Stale();
        if (applied.Disposition == LandSourceCheckpointDisposition.SourceStateChanged)
            throw new LandSourceResolutionConflictException();
        return Result.Refused(reason, diagnostic);
    }

    private async Task<Result?> CheckpointAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, CancellationToken ct)
    {
        var applied = await _writer.ApplyAsync(task, request, baseline, LandSourceCheckpointPatch.From(request), ct);
        if (applied.Disposition == LandSourceCheckpointDisposition.Applied) return null;
        if (applied.Disposition == LandSourceCheckpointDisposition.StaleRequest) return Result.Stale();
        throw new LandSourceResolutionConflictException();
    }

    private async Task CheckpointCallbackAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCheckpointBaseline baseline, CancellationToken ct)
    {
        var applied = await _writer.ApplyAsync(task, request, baseline, LandSourceCheckpointPatch.From(request), ct);
        if (applied.Disposition != LandSourceCheckpointDisposition.Applied)
            throw new InvalidOperationException("source_child_checkpoint_failed");
    }

    private async Task<string?> RecheckRemoteAsync(string repository, string sourceFullRef, string prefix,
        LandingSourceObservation accepted, CancellationToken ct)
    {
        var observed = await git.ObserveSourceAsync(repository, sourceFullRef, prefix, ct);
        if (!observed.Accepted) return observed.Reason ?? "source_remote_unreadable";
        return observed.Sha == accepted.Sha && observed.Fingerprint == accepted.Fingerprint
            ? null : "source_remote_changed";
    }

    private async Task<string?> TargetCheckoutAsync(AgentTaskLanding op, CancellationToken ct)
    {
        var rows = (await git.RegistrationsAsync(op.RepositoryPath, ct)).Where(r => r.Branch == op.TargetFullRef).ToList();
        if (rows.Count > 1) throw new InvalidOperationException("ambiguous_target_checkout");
        if (rows.Count == 0) return null;
        if (rows[0].Locked || rows[0].Prunable) throw new InvalidOperationException("target_registration_unavailable");
        return await git.CanonicalDirectoryAsync(rows[0].Path, ct);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
    private static string FullRef(string branch) =>
        branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

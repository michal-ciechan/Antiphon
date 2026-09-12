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
    public sealed record Result(AgentTaskLanding? Operation, string? Reason, bool CreatedOperation);

    public async Task<Result> ResolveAsync(AgentTask task, AgentTaskLandRequest request, RepositoryLease lease,
        CancellationToken ct)
    {
        await db.Entry(request).ReloadAsync(ct);
        if (!request.IsPending || task.CurrentLandRequestId != request.Id)
            return new(null, "stale_land_request", false);
        if (request.SchemaVersion is not 1 and not 2)
            return await RefuseAsync(request, "landing_schema_unsupported", null, null, null, ct);
        if (request.SchemaVersion != 2 || !GitObjectId.IsFull(request.ExpectedSourceSha))
            return await RefuseAsync(request, "legacy_review_binding_required", null, null, null, ct);

        if (task.RepoPath is null || task.WorktreePath is null || task.WorktreeBranch is null)
            return await RefuseAsync(request, "source_coordinates_missing", null, null, null, ct);
        var coordinates = new LandSourceCoordinates(task.Id, task.RepoPath, task.WorktreePath,
            FullRef(task.WorktreeBranch), FullRef(task.MergeTargetRef ?? "master"));
        if (request.SourceFullRefSnapshot is not null && request.SourceFullRefSnapshot != coordinates.SourceFullRef
            || request.TargetFullRefSnapshot is not null && request.TargetFullRefSnapshot != coordinates.TargetFullRef
            || request.RepositoryPathSnapshot is not null && !SamePath(request.RepositoryPathSnapshot, coordinates.RepositoryPath)
            || request.WorktreePathSnapshot is not null && !SamePath(request.WorktreePathSnapshot, coordinates.WorktreePath))
            return await RefuseAsync(request, "request_coordinates_changed", null, null, null, ct);

        var common = await git.CommonDirectoryAsync(coordinates.RepositoryPath, ct);
        if (!leases.Owns(lease, common))
            return await RefuseAsync(request, "repository_lease_required", null, null, null, ct);

        if (request.SourceAdvanceChildOperation is not null)
        {
            if (request.SourceAdvanceChildProcessId is null || request.SourceAdvanceChildStartTicks is null)
                return await RefuseAsync(request, "interrupted_process_requires_inspection", null, null, null, ct);
            var alive = await git.IsProcessAliveAsync(request.SourceAdvanceChildProcessId.Value,
                request.SourceAdvanceChildStartTicks.Value, ct);
            if (alive != false)
                return await RefuseAsync(request, "interrupted_process_requires_inspection", null, null, null, ct);
            request.SourceAdvanceChildOperation = null;
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            await SaveRequestAsync(request, ct);
        }

        var expected = request.ExpectedSourceSha!;
        if (request.SourceResolutionState == LandSourceResolutionState.Resolved
            && request.ResolvedSourceSha == expected)
        {
            var existing = task.ActiveLandingId is Guid id
                ? await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == id && o.TaskId == task.Id, ct)
                : null;
            if (existing is not null) return new(existing, null, false);
            return await CreateOperationAsync(task, request, coordinates, lease, ct);
        }

        var inspection = await git.InspectAsync(coordinates, ct);
        if (!inspection.Accepted)
            return await RefuseAsync(request, inspection.Reason ?? "source_unknown", inspection.Snapshot?.HeadSha, null, null, ct);
        var local = inspection.Snapshot!;
        if (request.SourceResolutionState == LandSourceResolutionState.AdvanceStarted)
        {
            if (local.HeadSha == expected)
            {
                request.SourceResolutionState = LandSourceResolutionState.Resolved;
                request.ResolvedSourceSha = expected;
                await SaveRequestAsync(request, ct);
                return await CreateOperationAsync(task, request, coordinates, lease, ct);
            }

            if (request.LocalBeforeSha is { } savedL && local.HeadSha == savedL && request.ResolvedSourceSha is null)
            {
                // Clean L retries the saved L→E pair only.
            }
            else
                return await RefuseAsync(request, "source_advance_head_unexpected", local.HeadSha,
                    request.RemoteSourceSha, expected, ct);
        }

        if (request.SourceResolutionState == LandSourceResolutionState.Observed
            && local.HeadSha == expected
            && request.LocalBeforeSha is { } recorded && recorded != expected)
            return await RefuseAsync(request, "source_advance_head_unexpected", local.HeadSha,
                request.RemoteSourceSha, expected, ct);

        var prefix = $"refs/antiphon/land/{task.Id:N}/{request.Id:N}/source-observed";
        var observed = await git.ObserveSourceAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, prefix, ct);
        if (!observed.Accepted)
            return await RefuseAsync(request, observed.Reason ?? "source_remote_unreadable", local.HeadSha, observed.Sha, null, ct);

        var remoteGraph = await ClassifyAsync(local.RegisteredPath, expected, observed.Sha!, ct);
        if (remoteGraph.Relationship is LandSourceRelationship.Unavailable or LandSourceRelationship.Unknown)
            return await RefuseAsync(request, remoteGraph.Reason ?? "source_remote_ancestry_error", local.HeadSha, observed.Sha, null, ct);
        if (remoteGraph.Relationship == LandSourceRelationship.Diverged)
            return await RefuseObservedAsync(request, local, observed, remoteGraph, "source_remote_diverged", ct);
        if (remoteGraph.Relationship == LandSourceRelationship.Behind)
            return await RefuseObservedAsync(request, local, observed, remoteGraph, "reviewed_source_mismatch", ct, observed.Sha);

        var localGraph = await ClassifyAsync(local.RegisteredPath, local.HeadSha, expected, ct);
        if (localGraph.Relationship is LandSourceRelationship.Unavailable or LandSourceRelationship.Unknown)
            return await RefuseAsync(request, localGraph.Reason ?? "source_remote_ancestry_error", local.HeadSha, observed.Sha, null, ct);

        var predecessorOp = task.ActiveLandingId is Guid activeId
            ? await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == activeId && o.TaskId == task.Id, ct)
            : null;
        var derivation = predecessorOp is { RebasedSourceSha: { } prepared } && prepared == local.HeadSha
            && predecessorOp.OriginalSourceSha == expected;

        var needFf = localGraph.Relationship == LandSourceRelationship.Behind;
        string? candidate = localGraph.Relationship == LandSourceRelationship.Equal || needFf || derivation
            ? expected : null;
        if (candidate is null)
            return await RefuseObservedAsync(request, local, observed, localGraph, "reviewed_source_mismatch", ct, local.HeadSha);

        request.LocalBeforeSha = local.HeadSha;
        request.RemoteSourceSha = observed.Sha;
        request.RemoteSourceRef = coordinates.SourceFullRef;
        request.RemoteSourceFingerprint = observed.Fingerprint;
        request.SourceObservationRef = observed.ObservationRef;
        request.SourceObservedAt = clock.GetUtcNow().UtcDateTime;
        request.CandidateSourceSha = candidate;
        request.SourceRelationship = needFf ? LandSourceRelationship.Behind
            : remoteGraph.Relationship == LandSourceRelationship.LocalAhead ? LandSourceRelationship.LocalAhead
            : localGraph.Relationship == LandSourceRelationship.Equal ? LandSourceRelationship.Equal
            : LandSourceRelationship.LocalAhead;
        request.SourceCommonDirectory = local.CommonDirectory;
        request.SourceWorktreePath = local.RegisteredPath;
        request.SourceGitDirectory = local.GitDirectory;
        request.SourceResolutionState = LandSourceResolutionState.Observed;
        await SaveRequestAsync(request, ct);

        var afterObserved = await RecheckRemoteAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, prefix,
            observed, ct);
        if (afterObserved is not null)
            return await RefuseAsync(request, afterObserved, local.HeadSha, request.RemoteSourceSha, expected, ct);

        if (needFf)
        {
            var still = await git.InspectAsync(coordinates, ct);
            if (!still.Accepted || still.Snapshot!.HeadSha != local.HeadSha
                || still.Snapshot.GitDirectory != local.GitDirectory
                || still.Snapshot.CommonDirectory != local.CommonDirectory)
                return await RefuseAsync(request, still.Reason ?? "source_changed", still.Snapshot?.HeadSha, observed.Sha, expected, ct);

            request.SourceResolutionState = LandSourceResolutionState.AdvanceStarted;
            request.SourceAdvanceChildOperation = "source-ff";
            await SaveRequestAsync(request, ct);
            var beforeMerge = await RecheckRemoteAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, prefix,
                observed, ct);
            if (beforeMerge is not null)
                return await RefuseAsync(request, beforeMerge, local.HeadSha, request.RemoteSourceSha, expected, ct);
            var merge = await git.RunOwnedAsync(local.RegisteredPath,
                ["-c", "merge.autoStash=false", "merge", "--ff-only", expected],
                async (pid, ticks, token) =>
                {
                    request.SourceAdvanceChildProcessId = pid;
                    request.SourceAdvanceChildStartTicks = ticks;
                    await SaveRequestAsync(request, token);
                }, ct);
            request.SourceAdvanceChildOperation = null;
            request.SourceAdvanceChildProcessId = null;
            request.SourceAdvanceChildStartTicks = null;
            await SaveRequestAsync(request, ct);
            if (!merge.Succeeded)
                return await RefuseAsync(request, "source_fast_forward_failed", local.HeadSha, observed.Sha, expected, ct);
            var after = await git.InspectAsync(coordinates, ct);
            if (!after.Accepted || after.Snapshot!.HeadSha != expected
                || after.Snapshot.GitDirectory != local.GitDirectory
                || after.Snapshot.CommonDirectory != local.CommonDirectory
                || after.Snapshot.RegisteredPath != local.RegisteredPath)
                return await RefuseAsync(request, after.Reason ?? "source_changed", after.Snapshot?.HeadSha, observed.Sha, expected, ct);
            var afterFf = await RecheckRemoteAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, prefix,
                observed, ct);
            if (afterFf is not null)
                return await RefuseAsync(request, afterFf, expected, request.RemoteSourceSha, expected, ct);
        }

        request.ResolvedSourceSha = expected;
        request.SourceResolutionState = LandSourceResolutionState.Resolved;
        await SaveRequestAsync(request, ct);
        return await CreateOperationAsync(task, request, coordinates, lease, ct);
    }

    private async Task<Result> CreateOperationAsync(AgentTask task, AgentTaskLandRequest request,
        LandSourceCoordinates coordinates, RepositoryLease lease, CancellationToken ct)
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
        catch (IOException) { return new(null, "remote_configuration_invalid", false); }
        string target;
        try { target = await CommitAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct); }
        catch (InvalidOperationException) { return new(null, "commit_lookup_failed", false); }
        var fresh = await git.InspectAsync(coordinates, ct);
        if (!fresh.Accepted) return new(null, fresh.Reason ?? "source_unknown", false);
        var snapshot = fresh.Snapshot!;
        AgentTaskLanding? previous = null;
        if (task.ActiveLandingId is Guid previousId)
            previous = await db.AgentTaskLandings.SingleAsync(o => o.Id == previousId, ct);
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = task.Id, SchemaVersion = 2, Phase = LandPhase.Inspected,
            CreatedAt = Now(), UpdatedAt = Now(), RepositoryPath = coordinates.RepositoryPath,
            WorktreePath = snapshot.RegisteredPath, CommonDirectory = snapshot.CommonDirectory,
            GitDirectory = snapshot.GitDirectory, SourceFullRef = coordinates.SourceFullRef,
            OriginalSourceSha = request.ExpectedSourceSha!, ReviewedSourceSha = request.ExpectedSourceSha,
            PreparationInputSha = previous?.RebasedSourceSha ?? request.ExpectedSourceSha,
            PreviousPreparationOperationId = previous is { RebasedSourceSha: { } prepared }
                && prepared != request.ExpectedSourceSha ? previous.Id : null,
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
            return new(null, ex.Message, false);
        }
        await using var transaction = previous is null ? null : await db.Database.BeginTransactionAsync(ct);
        if (previous is not null)
        {
            previous.Active = false;
            previous.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        db.AgentTaskLandings.Add(op);
        task.ActiveLandingId = op.Id;
        request.LandingOperationId = op.Id;
        task.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        _ = lease;
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

    private async Task<Result> RefuseObservedAsync(AgentTaskLandRequest request, LandSourceSnapshot local,
        LandingSourceObservation observed, LandingSourceGraph graph, string reason, CancellationToken ct,
        string? candidate = null)
    {
        request.LocalBeforeSha = local.HeadSha;
        request.RemoteSourceSha = observed.Sha;
        request.RemoteSourceFingerprint = observed.Fingerprint;
        request.SourceObservationRef = observed.ObservationRef;
        request.SourceObservedAt = clock.GetUtcNow().UtcDateTime;
        request.SourceRelationship = graph.Relationship;
        request.CandidateSourceSha = candidate;
        request.SourceRefusalReason = reason;
        await SaveRequestAsync(request, ct);
        return new(null, reason, false);
    }

    private async Task<Result> RefuseAsync(AgentTaskLandRequest request, string reason, string? local, string? remote,
        string? candidate, CancellationToken ct)
    {
        request.SourceRefusalReason = reason;
        request.LocalBeforeSha = local;
        request.RemoteSourceSha = remote;
        request.CandidateSourceSha = candidate;
        await SaveRequestAsync(request, ct);
        return new(null, reason, false);
    }

    private async Task<string?> RecheckRemoteAsync(string repository, string sourceFullRef, string prefix,
        LandingSourceObservation accepted, CancellationToken ct)
    {
        var observed = await git.ObserveSourceAsync(repository, sourceFullRef, prefix, ct);
        if (!observed.Accepted) return observed.Reason ?? "source_remote_unreadable";
        return observed.Sha == accepted.Sha && observed.Fingerprint == accepted.Fingerprint
            ? null : "source_remote_changed";
    }

    private async Task SaveRequestAsync(AgentTaskLandRequest request, CancellationToken ct)
    {
        request.ConcurrencyToken = Guid.NewGuid();
        request.LastProgressAt = clock.GetUtcNow().UtcDateTime;
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private async Task<string> CommitAsync(string repository, string revision, CancellationToken ct)
    {
        var result = await git.RunAsync(repository, ["rev-parse", "--verify", revision + "^{commit}"], ct);
        var sha = result.Output.Trim();
        if (!result.Succeeded || !GitObjectId.IsFull(sha)) throw new InvalidOperationException("commit_lookup_failed");
        return sha;
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

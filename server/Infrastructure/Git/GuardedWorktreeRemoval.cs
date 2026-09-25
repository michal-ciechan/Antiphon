using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>One deletion path. Unknown authority, identity, content or I/O preserves residue.</summary>
public sealed class GuardedWorktreeRemoval(ILandingGit git, IRepositoryMutationLease leases,
    IWorktreeRemovalEvidence evidence, WorktreeGuardedCleanup? cleanup = null,
    WorktreeIgnoredContentGate? ignored = null)
{
    public async Task<WorktreeRemoval> RemoveAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        if (request.Purpose == WorktreeRemovalPurpose.Verification)
            return await new GuardedVerificationRemoval(git, leases, evidence).RemoveAsync(request, ct);
        if (request.Purpose == WorktreeRemovalPurpose.Publication && request.CleanupContext is not null)
            return cleanup is null ? new(false, false, false, "cleanup_journal_required")
                : await cleanup.RemoveAsync(request, this, ct);
        var directory = await RemoveDirectoryAsync(request, null, TimeProvider.System, ct);
        return directory.Result.Residue is not null ? directory.Result
            : await CompleteBranchAsync(request, directory.Result, ct);
    }

    internal async Task<WorktreeDirectoryPass> RemoveDirectoryAsync(WorktreeRemovalRequest request,
        Func<CancellationToken, Task<bool>>? consumeSlot, TimeProvider clock, CancellationToken ct)
    {
        var source = request.Source;
        var directoryGone = false;
        var unregistered = false;
        WorktreeGitOutcome? outcome = null;
        long? failedAt = null;
        string? retainedDetail = null;
        WorktreeDirectoryPass Finish(string? reason, string? detail = null) => new(new(unregistered, directoryGone, false, reason)
            { Detail = detail ?? (reason is null ? retainedDetail : null) }, outcome, failedAt);
        try
        {
            if ((request.CleanupContext is not null || request.Purpose == WorktreeRemovalPurpose.SettledTask)
                && !IsAbsent(source.WorktreePath))
            {
                if (request.ManagedRoot is null || !WorktreeNativeIO.Within(source.WorktreePath, request.ManagedRoot)
                    || !LandingGit.PathsEqual(await git.CanonicalDirectoryAsync(source.WorktreePath, ct), source.WorktreePath)
                    || !LandingGit.PathsEqual(await git.CanonicalDirectoryAsync(request.ManagedRoot, ct), request.ManagedRoot))
                    return Finish("worktree_confinement_changed");
            }
            var reason = await AuthorityAsync(request, ct);
            if (reason is not null) return Finish(reason);
            var rows = await RegistrationsAsync(source.RepositoryPath, ct);
            var registration = rows.Where(r => LandingGit.PathsEqual(r.Path, source.WorktreePath)).ToList();
            // File.GetAttributes distinguishes absence from access/query errors.
            directoryGone = IsAbsent(source.WorktreePath);
            unregistered = registration.Count == 0;
            // Review 0c0b9a4e item 1: a tree an earlier pass set aside is finished first. Complete is
            // reported only once those bytes are gone, never merely because the path is absent.
            var aside = WorktreeSetAside.SetAsidePath(source.WorktreePath);
            var pending = WorktreeSetAside.Read(request.CommonDirectory, source.WorktreePath);
            if (pending is not null)
            {
                if (!LandingGit.PathsEqual(pending.WorktreePath, source.WorktreePath)
                    || !LandingGit.PathsEqual(pending.SetAsidePath, aside)
                    || !LandingGit.PathsEqual(pending.GitDirectory, request.GitDirectory))
                    return Finish("set_aside_record_mismatch", "set-aside: " + pending.SetAsidePath);
                if (IsAbsent(aside)) WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                else if (!directoryGone) return Finish("set_aside_pending", "set-aside: " + aside);
                else if (!unregistered)
                {
                    // The registration was never dropped, so nothing was deleted: put the tree back and
                    // decide from a fresh reading of it.
                    if (!WorktreeNoFollowDelete.Restore(aside, source.WorktreePath))
                        return Finish("worktree_removal_incomplete", "set-aside: " + aside);
                    WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    return await RemoveDirectoryAsync(request, consumeSlot, clock, ct);
                }
                else
                {
                    try { WorktreeNoFollowDelete.Delete(aside); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failedAt = clock.GetTimestamp();
                        return Finish("worktree_removal_incomplete", "set-aside: " + aside);
                    }
                    if (!IsAbsent(aside)) return Finish("worktree_removal_incomplete", "set-aside: " + aside);
                    WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    // The record is this removal's own receipt for the absent, unregistered tree.
                    return Finish(null);
                }
            }
            if (!directoryGone)
            {
                if (unregistered) return Finish("unregistered_directory");
                if (registration.Any(r => r.Locked)) return Finish("registration_locked");
                if (registration.Any(r => r.Prunable)) return Finish("registration_prunable");
                if (rows.Any(r => !LandingGit.PathsEqual(r.Path, source.WorktreePath)
                    && WorktreeNativeIO.Within(r.Path, source.WorktreePath)))
                    return Finish("nested_registration");
                var inspection = await git.InspectAsync(source, LandInspectionScope.Full, ct);
                if (!Matches(inspection, request)) return Finish(inspection.Reason ?? "source_changed");
                // Removing the tree ALSO deletes its ignored files. Only the CARD-0665 allowlist
                // grants that: protected content refuses, evidence is retained before reading 2.
                var first = Classify(inspection.Snapshot!);
                if (first.Protected.Length != 0)
                    return Finish("ignored_content_preserved", WorktreeIgnoredContentGate.ProtectedDetail(first.Protected));
                // Nothing is copied or deleted through a link at either reading.
                var firstLinks = WorktreeIgnoredContentGate.ReparsePoints(source.WorktreePath, first);
                if (firstLinks.Length != 0)
                    return Finish("ignored_reparse_point", WorktreeIgnoredContentGate.ReparseDetail(firstLinks));
                if (ignored is not null)
                {
                    var retained = await ignored.RetainAsync(request, first.Evidence, ct);
                    if (retained.Reason is not null) return Finish(retained.Reason);
                    retainedDetail = WorktreeIgnoredContentGate.RetainedDetail(retained);
                }
                reason = await AuthorityAsync(request, ct);
                if (reason is not null) return Finish(reason);
                var final = await git.InspectAsync(source, LandInspectionScope.Full, ct);
                if (!Matches(final, request)) return Finish(final.Reason ?? "source_changed");
                var second = Classify(final.Snapshot!);
                if (second.Protected.Length != 0)
                    return Finish("ignored_content_preserved", WorktreeIgnoredContentGate.ProtectedDetail(second.Protected));
                var finalLinks = WorktreeIgnoredContentGate.ReparsePoints(source.WorktreePath, second);
                if (finalLinks.Length != 0)
                    return Finish("ignored_reparse_point", WorktreeIgnoredContentGate.ReparseDetail(finalLinks));
                // Disposable churn is allowed; evidence must be exactly what was retained.
                if (!second.Evidence.SequenceEqual(first.Evidence, StringComparer.Ordinal)) return Finish("ignored_content_changed");
                if (consumeSlot is not null && !await consumeSlot(ct)) return Finish("cleanup_command_slot_spent");
                // The final status/identity read can race a task-coordinate or receipt revision.
                // Revalidate durable authority immediately before deletion without reusing a tracked row.
                reason = await AuthorityAsync(request, ct, refreshRemote: false);
                if (reason is not null) return Finish(reason);
                // Review 5b79328d item 1: Git is never allowed to traverse the tree, because a link
                // swapped in after the last reading would carry its deletion outside. The tree is set
                // aside (a rename moves the entry itself), Git's administrative entry for the absent
                // directory is dropped, and only then are the set-aside bytes deleted without
                // following a link. Until the registration is gone the tree can be put back unchanged.
                // Review 0c0b9a4e: the set-aside name is recorded before the move so a later pass can
                // finish it, and Git is never handed the path, which is free once the tree moved.
                if (!IsAbsent(aside)) return Finish("set_aside_unrecorded", "set-aside: " + aside);
                WorktreeSetAside.Record(request.CommonDirectory, new(source.WorktreePath, aside, request.GitDirectory,
                    clock.GetUtcNow().UtcDateTime));
                try { WorktreeNoFollowDelete.MoveAside(source.WorktreePath, aside); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A handle or working directory inside the tree refuses the rename; nothing moved.
                    WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    var code = ex.HResult & 0xFFFF;
                    outcome = new("worktree move-aside", code, $"move_error_{code}", ex.GetType().Name,
                        clock.GetUtcNow().UtcDateTime, true);
                    failedAt = clock.GetTimestamp();
                    return Finish("worktree_remove_failed");
                }
                string? failure;
                try { (failure, outcome) = await UnregisterAsync(source, request.GitDirectory, clock, ct); }
                catch
                {
                    // Cancellation or an unexpected fault still leaves the tree where it was.
                    if (WorktreeNoFollowDelete.Restore(aside, source.WorktreePath))
                        WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    throw;
                }
                unregistered = failure is null;
                if (failure is not null)
                {
                    failedAt = clock.GetTimestamp();
                    var restored = WorktreeNoFollowDelete.Restore(aside, source.WorktreePath);
                    if (restored) WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    directoryGone = IsAbsent(source.WorktreePath);
                    return restored ? Finish(failure) : Finish("worktree_removal_incomplete", "set-aside: " + aside);
                }
                try { WorktreeNoFollowDelete.Delete(aside); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The record stays, so a later pass resumes this deletion.
                    failedAt = clock.GetTimestamp();
                    directoryGone = IsAbsent(source.WorktreePath);
                    return Finish("worktree_removal_incomplete", "set-aside: " + aside);
                }
                directoryGone = IsAbsent(source.WorktreePath);
                if (!directoryGone || !IsAbsent(aside))
                { failedAt = clock.GetTimestamp(); return Finish("worktree_removal_incomplete", "set-aside: " + aside); }
                WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
            }
            else if (!unregistered)
                return Finish("missing_source_without_cleanup_receipt");
            else if (request.Purpose == WorktreeRemovalPurpose.Publication)
                return Finish(null);
            else if (request.Purpose == WorktreeRemovalPurpose.SettledTask && request.HasDeletionIntent)
                return Finish(null);
            else
                return Finish("missing_source_without_cleanup_receipt");
            return Finish(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            if (outcome is not null) failedAt ??= clock.GetTimestamp();
            return Finish("cleanup_inspection_error");
        }
    }

    /// <summary>
    /// Drops Git's administrative entry for the set-aside tree; the path itself is never handed to
    /// Git (review 0c0b9a4e item 2). Returns null once the registration is gone, else the residue;
    /// the caller puts the tree back.
    /// </summary>
    private async Task<(string? Failure, WorktreeGitOutcome? Outcome)> UnregisterAsync(LandSourceCoordinates source,
        string gitDirectory, TimeProvider clock, CancellationToken ct)
    {
        // Something recreated the path; it is not this tree and is left alone.
        if (!IsAbsent(source.WorktreePath)) return ("worktree_removal_incomplete", null);
        LandingGitResult removed;
        WorktreeGitOutcome outcome;
        try
        {
            removed = await git.UnregisterWorktreeAsync(source.RepositoryPath, source.WorktreePath, gitDirectory, ct);
            outcome = new("worktree unregister", removed.ExitCode, removed.Succeeded ? "unregister_exit_0"
                : $"unregister_exit_{removed.ExitCode}", null, clock.GetUtcNow().UtcDateTime, true);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return (ex is TimeoutException ? "worktree_remove_timeout" : "worktree_remove_failed",
                new("worktree unregister", null, ex is TimeoutException ? "git_timeout" : "git_command_error",
                    ex.GetType().Name, clock.GetUtcNow().UtcDateTime, false));
        }
        if (!removed.Succeeded) return ("worktree_remove_failed", outcome);
        var rows = await RegistrationsAsync(source.RepositoryPath, ct);
        return (rows.Any(r => LandingGit.PathsEqual(r.Path, source.WorktreePath)) ? "worktree_removal_incomplete" : null, outcome);
    }

    internal async Task<WorktreeRemoval> CompleteBranchAsync(WorktreeRemovalRequest request, WorktreeRemoval directory, CancellationToken ct)
    {
        var source = request.Source;
        var branchGone = false;
        WorktreeRemoval Refuse(string reason) => directory with { BranchDeleted = branchGone, Residue = reason };
        try
        {
            // Refresh authority and all checkout registrations before the exact old-SHA delete.
            var reason = await AuthorityAsync(request, ct);
            if (reason is not null) return Refuse(reason);
            if (!IsAbsent(source.WorktreePath)) return Refuse("source_recreated");
            var rows = await RegistrationsAsync(source.RepositoryPath, ct);
            if (rows.Any(r => r.Branch == source.SourceFullRef)) return Refuse("source_checked_out");
            var exists = await git.RunAsync(source.RepositoryPath, ["show-ref", "--exists", source.SourceFullRef], ct);
            if (exists.ExitCode == 2)
            {
                if (request.Purpose != WorktreeRemovalPurpose.Publication) return Refuse("source_ref_missing");
                branchGone = true;
            }
            else
            {
                if (!exists.Succeeded) return Refuse("source_ref_error");
                var current = await git.RunAsync(source.RepositoryPath,
                    ["show-ref", "--verify", "--hash", source.SourceFullRef], ct);
                if (!current.Succeeded || current.Output.Trim() != request.ExpectedSourceSha)
                    return Refuse("source_changed");
                var deleted = await git.RunAsync(source.RepositoryPath,
                    ["update-ref", "--no-deref", "-d", source.SourceFullRef, request.ExpectedSourceSha], ct);
                if (!deleted.Succeeded) return Refuse("branch_delete_failed");
                var absent = await git.RunAsync(source.RepositoryPath, ["show-ref", "--exists", source.SourceFullRef], ct);
                branchGone = absent.ExitCode == 2;
                if (!branchGone) return Refuse("branch_deletion_unconfirmed");
            }
            return directory with { BranchDeleted = branchGone, Residue = null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            return Refuse("cleanup_inspection_error");
        }
    }

    private async Task<string?> AuthorityAsync(WorktreeRemovalRequest request, CancellationToken ct, bool refreshRemote = true)
    {
        if (request.Purpose is not (WorktreeRemovalPurpose.Publication or WorktreeRemovalPurpose.LocalMerge
            or WorktreeRemovalPurpose.SettledTask))
            return "removal_purpose_unsupported";
        var source = request.Source;
        var common = await git.CommonDirectoryAsync(source.RepositoryPath, ct);
        if (!LandingGit.PathsEqual(common, request.CommonDirectory) || !leases.Owns(request.Lease, common))
            return "repository_lease_required";
        if (!LandingGit.IsOid(request.ExpectedSourceSha) || !LandingGit.IsOid(request.ExpectedTargetSha)
            || source.SourceFullRef == source.TargetFullRef) return "invalid_removal_identity";
        if (request.Purpose == WorktreeRemovalPurpose.SettledTask)
        {
            if (request.RetirementId is not Guid retirementId) return "retirement_receipt_required";
            var retirement = await evidence.ReadRetirementAsync(retirementId, ct);
            if (retirement is null || retirement.Id != retirementId || !retirement.Active
                || retirement.TaskId != source.TaskId || retirement.SourceFullRef != source.SourceFullRef
                || retirement.SourceSha != request.ExpectedSourceSha
                || !LandingGit.PathsEqual(retirement.RepositoryPath, source.RepositoryPath)
                || !LandingGit.PathsEqual(retirement.WorktreePath, source.WorktreePath)
                || !LandingGit.PathsEqual(retirement.GitDirectory, request.GitDirectory)
                || !LandingGit.PathsEqual(retirement.CommonDirectory, common)
                || retirement.State is WorktreeRetirementState.Revoked)
                return "retirement_receipt_mismatch";
            if (!refreshRemote) return null;
            var destination = new LandingDestination(retirement.RemoteName, retirement.DestinationFullRef,
                retirement.RemoteFingerprint);
            var retirementObserved = await git.ObserveRetirementAsync(source.RepositoryPath, destination,
                request.ExpectedSourceSha, retirement.Id, "cleanup-observed", ct);
            return retirementObserved.Reason ?? (retirementObserved.ContainsSource ? null : "remote_no_longer_contains_source");
        }
        if (request.Purpose == WorktreeRemovalPurpose.LocalMerge)
        {
            if (!request.TargetCheckoutRecorded) return "local_target_identity_required";
            var targets = (await git.RegistrationsAsync(source.RepositoryPath, ct))
                .Where(r => r.Branch == source.TargetFullRef).ToList();
            if (targets.Count > 1 || targets.Any(r => r.Locked || r.Prunable)) return "local_target_unavailable";
            var checkout = targets.Count == 0 ? null : await git.CanonicalDirectoryAsync(targets[0].Path, ct);
            if (checkout is null ? request.TargetCheckoutPath is not null
                : request.TargetCheckoutPath is null || !LandingGit.PathsEqual(checkout, request.TargetCheckoutPath))
                return "local_target_checkout_changed";
            if (checkout is not null)
            {
                if (await git.HasActiveSequencerAsync(checkout, ct)) return "local_target_active_sequencer";
                var symbolic = await git.RunAsync(checkout, ["symbolic-ref", "-q", "HEAD"], ct);
                var head = await git.RunAsync(checkout, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
                var status = await git.RunAsync(checkout, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
                if (!symbolic.Succeeded || symbolic.Output.Trim() != source.TargetFullRef || !head.Succeeded
                    || head.Output.Trim() != request.ExpectedTargetSha || !status.Succeeded || status.Output.Length != 0)
                    return "local_target_dirty_or_changed";
            }
            var targetSymbolic = await git.RunAsync(source.RepositoryPath,
                ["symbolic-ref", "-q", source.TargetFullRef], ct);
            if (targetSymbolic.ExitCode != 1) return "local_target_symbolic_or_unresolved";
            var target = await git.RunAsync(source.RepositoryPath,
                ["rev-parse", "--verify", source.TargetFullRef + "^{commit}"], ct);
            if (!target.Succeeded || target.Output.Trim() != request.ExpectedTargetSha) return "local_target_changed";
            var ancestry = await git.RunAsync(source.RepositoryPath,
                ["merge-base", "--is-ancestor", request.ExpectedSourceSha, request.ExpectedTargetSha], ct);
            return ancestry.ExitCode == 0 ? null : "local_merge_unconfirmed";
        }
        if (request.LandingId is not Guid id) return "publication_receipt_required";
        var op = await evidence.ReadAsync(id, ct);
        if (op is null || op.Id != id || !op.Active || !new AgentTaskLandingState().HasPublication(op)
            || op.TaskId != source.TaskId || op.SourceFullRef != source.SourceFullRef
            || op.TargetFullRef != source.TargetFullRef || op.TargetBeforeSha != request.ExpectedTargetSha
            || op.ExpectedDeletionSha != request.ExpectedSourceSha
            || op.VerifiedSourceSha != request.ExpectedSourceSha || op.CleanupStartedAt is null
            || op.Phase is not (LandPhase.CleanupStarted or LandPhase.Complete)
            || !LandingGit.PathsEqual(op.RepositoryPath, source.RepositoryPath)
            || !LandingGit.PathsEqual(op.WorktreePath, source.WorktreePath)
            || !LandingGit.PathsEqual(op.GitDirectory, request.GitDirectory)
            || !LandingGit.PathsEqual(op.CommonDirectory, common)) return "publication_receipt_mismatch";
        var pins = new List<(string Name, string Sha)> { ("source", op.OriginalSourceSha), ("target-before", op.TargetBeforeSha) };
        if (op.RebasedSourceSha is not null) pins.Add(("prepared", op.RebasedSourceSha));
        foreach (var pin in pins)
        {
            var resolved = await git.RunAsync(source.RepositoryPath,
                ["show-ref", "--verify", "--hash", op.RecoveryRefPrefix + "/" + pin.Name], ct);
            if (!resolved.Succeeded || resolved.Output.Trim() != pin.Sha) return "recovery_pin_changed";
        }
        if (!refreshRemote) return null; // The preceding authority read already refreshed remote proof.
        var observed = await git.ObserveAsync(source.RepositoryPath,
            new(op.RemoteName, op.DestinationFullRef, op.RemoteFingerprint), request.ExpectedSourceSha,
            op.RecoveryRefPrefix + "/cleanup-observed", ct);
        return observed.Reason ?? (observed.ContainsSource ? null : "remote_no_longer_contains_source");
    }

    private async Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct)
    {
        var result = await git.RunAsync(repository, ["worktree", "list", "--porcelain", "-z"], ct);
        if (!result.Succeeded) throw new IOException("registration_query_error");
        return LandingGit.ParseRegistrations(result.Output);
    }

    private static bool Matches(LandSourceInspection result, WorktreeRemovalRequest expected) => result.Accepted
        && result.Snapshot!.HeadSha == expected.ExpectedSourceSha
        && LandingGit.PathsEqual(result.Snapshot.CommonDirectory, expected.CommonDirectory)
        && LandingGit.PathsEqual(result.Snapshot.GitDirectory, expected.GitDirectory);

    /// <summary>CARD-0665 D-6: without a gate every ignored path is protected, as before.</summary>
    private WorktreeIgnoredContent Classify(LandSourceSnapshot snapshot) => ignored?.Classify(snapshot)
        ?? new([], [], [.. snapshot.IgnoredPaths.Order(StringComparer.Ordinal)]);

    private static bool IsAbsent(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }
}

internal sealed record WorktreeDirectoryPass(WorktreeRemoval Result, WorktreeGitOutcome? Outcome, long? FailureTimestamp);

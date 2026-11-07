using System.Collections.Immutable;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>One deletion path. Unknown authority, identity, content or I/O preserves residue.</summary>
public sealed class GuardedWorktreeRemoval(ILandingGit git, IRepositoryMutationLease leases,
    IWorktreeRemovalEvidence evidence, WorktreeGuardedCleanup? cleanup = null,
    WorktreeIgnoredContentGate? ignored = null, WorktreePathLimit? pathLimit = null)
{
    /// <summary>The set-aside renames and delete; a test injects a Windows sharing violation here.</summary>
    internal WorktreeRenames Renames { get; init; } = WorktreeRenames.System;

    /// <summary>CARD-1714: the longest path the no-follow delete can open; a test lowers it.</summary>
    internal WorktreePathLimit PathLimit { get; init; } = pathLimit ?? WorktreePathLimit.System;

    public async Task<WorktreeRemoval> RemoveAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        if (request.Source.DesktopWorkspaceKind == DesktopWorkspaceKind.RefOnly)
            return DesktopWorkspaceCleanup.NotRequired(request.Source.TaskRefWorkspaceBindingId);
        if (request.Source.DesktopWorkspaceKind != DesktopWorkspaceKind.Checkout || request.Source.TaskRefWorkspaceBindingId is not null)
            return new(false, false, false, "desktop_workspace_binding_contradictory");
        var currentOwner = await evidence.ReadTaskAsync(request.Source.TaskId, ct);
        if (currentOwner is not null && DesktopWorkspaceCleanup.Reason(currentOwner) is { } desktopReason)
            return new(false, false, false, desktopReason);
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
        string? setAsideDetail = null;
        WorktreeDirectoryPass Finish(string? reason, string? detail = null) => new(new(unregistered, directoryGone, false, reason)
            { Detail = detail ?? (reason is null ? retainedDetail : setAsideDetail) }, outcome, failedAt);
        WorktreeDirectoryPass Preserve(NestedRepositoryPreservedException preserved, string aside)
        {
            directoryGone = IsAbsent(source.WorktreePath);
            outcome = null; // Intentional preservation needs no delete-failure diagnostics or retry.
            return Finish("ignored_content_preserved", WorktreeCleanupPresentation.Clip(
                WorktreeIgnoredContentGate.ProtectedDetail(preserved.Paths) + "; set-aside: " + aside, 400));
        }
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
                    // decide from a fresh reading of it. A different directory at the set-aside path stays there.
                    var hold = RestoreCustody(pending);
                    if (hold is not null) return Finish(hold, "set-aside: " + aside);
                    if (!WorktreeNoFollowDelete.Restore(aside, source.WorktreePath))
                        return Finish("worktree_removal_incomplete", "set-aside: " + aside);
                    WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    // Restoring the directory does not change Git's administrative stamp. Refresh
                    // the cached prunable flag before inspecting the restored checkout.
                    await git.LiveRegistrationsAsync(source.RepositoryPath, ct);
                    return await RemoveDirectoryAsync(request, consumeSlot, clock, ct);
                }
                else
                {
                    // CARD-1717 D-1: a resumed set-aside is a later pass. Re-read after the registration await,
                    // then D-4: the receipt names a path. Delete only the directory inode and listing it recorded.
                    // Nothing below awaits before the delete.
                    var resumed = await LiveAuthorizationAsync(request, ct);
                    if (resumed is not null) return Finish(resumed, "set-aside: " + aside);
                    var custody = CustodyOf(pending);
                    if (custody is not null) return Finish(custody, "set-aside: " + aside);
                    try { DeleteApproved(aside, pending); }
                    catch (NestedRepositoryPreservedException preserved) { return Preserve(preserved, aside); }
                    catch (ApprovedTreeRejectedException rejected) { return Finish(rejected.Reason, "set-aside: " + aside); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failedAt = clock.GetTimestamp();
                        return Finish("worktree_removal_incomplete", DeleteFailedDetail(aside, ex));
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
                IReadOnlyList<RetainedWorktreeFile>? retainedFiles = null;
                if (ignored is not null)
                {
                    var retained = await ignored.RetainAsync(request, first.Evidence, ct);
                    if (retained.Reason is not null) return Finish(retained.Reason);
                    retainedFiles = retained.Files;
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
                // Disposable churn is allowed; evidence names, then the bytes retention already hashed, must match.
                if (!second.Evidence.SequenceEqual(first.Evidence, StringComparer.Ordinal)) return Finish("ignored_content_changed");
                if (EvidenceProblem(source.WorktreePath, retainedFiles) is { } evidenceProblem) return Finish(evidenceProblem);
                // CARD-1714: the no-follow delete cannot open an entry past the platform limit, so a deeper tree would
                // be set aside, unregistered and then stuck half deleted on every retry. Refuse while nothing has
                // moved, measuring each entry under its set-aside name (longer than its own).
                if (PathLimit.FirstTooLong(source.WorktreePath, aside) is { } tooLong)
                    return Finish("worktree_path_too_long", WorktreeCleanupPresentation.Clip("path: " + tooLong, 400));
                if (consumeSlot is not null && !await consumeSlot(ct)) return Finish("cleanup_command_slot_spent");
                // The final status/identity read can race a task-coordinate or receipt revision.
                // Revalidate durable authority immediately before deletion without reusing a tracked row.
                reason = await AuthorityAsync(request, ct, refreshRemote: false);
                if (reason is not null) return Finish(reason);
                // CARD-1717 D-2: status already returned. A regular file that is neither tracked nor
                // ignored appeared after that proof and must still be here when deletion starts.
                var late = await LateUntrackedAsync(source.WorktreePath, final.Snapshot!, ct);
                if (late is not null) return Finish(late);
                // Review 5b79328d item 1: Git is never allowed to traverse the tree, because a link
                // swapped in after the last reading would carry its deletion outside. The tree is set
                // aside (a rename moves the entry itself), Git's administrative entry for the absent
                // directory is dropped, and only then are the set-aside bytes deleted without
                // following a link. Until the registration is gone the tree can be put back unchanged.
                // Review 0c0b9a4e: the set-aside name is recorded before the move so a later pass can
                // finish it, and Git is never handed the path, which is free once the tree moved.
                // CARD-1717 round 2: tracked bytes are bound to the proven commit, and the receipt keeps
                // content hashes plus the retained copies, before anything moves.
                // CARD-1708: the unregister drops the tree's HEAD reflog and the branch delete its branch reflog. A commit
                // only they reach gets a salvage ref first; a failure, or the switch turned off, refuses while nothing moved.
                if (request.SalvageScope is not null)
                {
                    var salvaged = await WorktreeReflogSalvage.SalvageAsync(git, source, request.SalvageScope,
                        c => LiveAuthorizationAsync(request, c), ct);
                    if (salvaged is not null) return Finish(salvaged);
                }
                if (!IsAbsent(aside)) return Finish("set_aside_unrecorded", "set-aside: " + aside);
                var bound = await BindContentAsync(request, source.WorktreePath, second, retainedFiles, ct);
                if (bound.Reason is not null) return Finish(bound.Reason);
                // CARD-1717 D-1: the inspections and the content binding above are awaits. Re-read the switch
                // last, before the receipt and the move-aside; nothing awaits between this read and the rename.
                reason = await LiveAuthorizationAsync(request, ct);
                if (reason is not null) return Finish(reason);
                var identity = WorktreeNoFollowDelete.DirectoryIdentity(source.WorktreePath);
                var seal = WorktreeSetAside.ListingSeal(source.WorktreePath);
                var recorded = new WorktreeSetAsideRecord(source.WorktreePath, aside, request.GitDirectory,
                    clock.GetUtcNow().UtcDateTime, identity, seal, bound.Entries, bound.Evidence);
                WorktreeSetAside.Record(request.CommonDirectory, recorded, Renames);
                try { WorktreeNoFollowDelete.MoveAside(source.WorktreePath, aside, Renames); }
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
                // The rename can admit a new file or a different directory before Git's entry is dropped.
                var drifted = CustodyOf(recorded);
                if (drifted is null) drifted = await LateUntrackedAsync(aside, final.Snapshot!, ct);
                if (drifted is null) drifted = await TrackedBytesAsync(aside, request,
                    new Dictionary<string, BoundReading>(StringComparer.Ordinal), ct);
                // CARD-1717 D-1: those checks are awaits and the unregister drops Git's entry. A switch turned off
                // here puts the tree back; nothing awaits between this read and the unregister command.
                if (drifted is null) drifted = await LiveAuthorizationAsync(request, ct);
                if (drifted is not null)
                {
                    var restored = await RestoreRegisteredTreeAsync();
                    return restored is null ? Finish(drifted, "set-aside: " + aside) : Finish(restored, "set-aside: " + aside);
                }
                string? failure;
                setAsideDetail = "set-aside: " + aside;
                // CARD-0824 S4a round 3 Review B-1: unregister awaits its own reads before the entry's move; the
                // switch is read again there, after them.
                string? stoppedAtMove = null;
                try
                {
                    using (LandingGit.GateDestructiveStep(async c => stoppedAtMove = await LiveAuthorizationAsync(request, c)))
                        (failure, outcome) = await UnregisterAsync(source, request.GitDirectory, clock, ct);
                }
                catch
                {
                    // The admin move may have completed before its acknowledgement or subsequent
                    // registration read failed. Restore only a positively observed live registration;
                    // absence or uncertainty keeps the recorded tree for the next pass.
                    await RestoreRegisteredTreeAsync();
                    throw;
                }
                unregistered = failure is null;
                if (stoppedAtMove is not null)
                {
                    // Nothing moved; this is a refusal, not a failed command.
                    outcome = null;
                    failure = stoppedAtMove;
                }
                else if (failure is not null) failedAt = clock.GetTimestamp();
                if (failure is not null)
                {
                    var restored = await RestoreRegisteredTreeAsync();
                    return restored is null ? Finish(failure) : Finish(restored, "set-aside: " + aside);
                }
                // CARD-1717 D-1: unregister is an await. Re-read before the set-aside delete, then D-2/D-3:
                // after the registration is gone the tree is held, not put back. A nested repository that
                // appeared in this window still falls through to the delete. Nothing below awaits before it.
                var stopped = await LiveAuthorizationAsync(request, ct);
                if (stopped is not null)
                {
                    directoryGone = IsAbsent(source.WorktreePath);
                    return Finish(stopped, "set-aside: " + aside);
                }
                var held = CustodyOf(recorded);
                if (held is not null)
                {
                    directoryGone = IsAbsent(source.WorktreePath);
                    return Finish(held, "set-aside: " + aside);
                }
                try { DeleteApproved(aside, recorded); }
                catch (NestedRepositoryPreservedException preserved) { return Preserve(preserved, aside); }
                catch (ApprovedTreeRejectedException rejected) { return Finish(rejected.Reason, "set-aside: " + aside); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The record stays, so a later pass resumes this deletion.
                    failedAt = clock.GetTimestamp();
                    directoryGone = IsAbsent(source.WorktreePath);
                    return Finish("worktree_removal_incomplete", DeleteFailedDetail(aside, ex));
                }
                directoryGone = IsAbsent(source.WorktreePath);
                if (!directoryGone || !IsAbsent(aside))
                { failedAt = clock.GetTimestamp(); return Finish("worktree_removal_incomplete", "set-aside: " + aside); }
                WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);

                async Task<string?> RestoreRegisteredTreeAsync()
                {
                    directoryGone = IsAbsent(source.WorktreePath);
                    // The caller token is often already cancelled: that is the outage, not an
                    // unreadable registration. Observe without it so a registration that was never
                    // dropped is put back. A failed read still keeps the set-aside tree.
                    var observe = ct.IsCancellationRequested ? CancellationToken.None : ct;
                    try
                    {
                        var current = await RegistrationsAsync(source.RepositoryPath, observe);
                        unregistered = !current.Any(r => LandingGit.PathsEqual(r.Path, source.WorktreePath));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                        or TimeoutException or OperationCanceledException)
                    {
                        // A failed query is not evidence that the administrative move failed.
                        return "worktree_removal_incomplete";
                    }
                    if (unregistered) return "worktree_removal_incomplete";
                    var hold = RestoreCustody(recorded);
                    if (hold is not null) return hold;
                    if (!WorktreeNoFollowDelete.Restore(aside, source.WorktreePath)) return "worktree_removal_incomplete";
                    directoryGone = false;
                    setAsideDetail = null;
                    WorktreeSetAside.TryClear(request.CommonDirectory, source.WorktreePath);
                    await git.LiveRegistrationsAsync(source.RepositoryPath, observe);
                    return null;
                }
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
    /// the caller rechecks registration before putting the tree back.
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
        if (!rows.Any(r => LandingGit.PathsEqual(r.Path, source.WorktreePath))) return (null, outcome);
        // CARD-0721: the administrative entry is the registration. A list that still names the
        // path after that entry is gone is stale. Restoring from it leaves an unregistered
        // directory and deletes the set-aside record.
        if (!RegistrationAdminPresent(gitDirectory)) return (null, outcome);
        return ("worktree_removal_incomplete", outcome);
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
            if (rows.Any(r => r.Branch == source.SourceFullRef && LiveCheckout(r, request))) return Refuse("source_checked_out");
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
                // CARD-1717 D-1: the ref reads are awaits. Re-read before the local branch delete.
                var live = await LiveAuthorizationAsync(request, ct);
                if (live is not null) return Refuse(live);
                // CARD-0824 S4a round 3 Review B-1: the child journal is written (awaits) after that read; the switch
                // is read again there, last before the git child starts.
                string? stoppedAtStart = null;
                LandingGitResult deleted;
                using (LandingGit.GateDestructiveStep(async c => stoppedAtStart = await LiveAuthorizationAsync(request, c)))
                    deleted = await git.RunAsync(source.RepositoryPath,
                        ["update-ref", "--no-deref", "-d", source.SourceFullRef, request.ExpectedSourceSha], ct);
                if (stoppedAtStart is not null) return Refuse(stoppedAtStart);
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

    /// <summary>CARD-1717 B-B-1: a system release reached the remover without its live switch.</summary>
    internal const string LiveAuthorizationRequired = "legacy_release_authorization_required";

    /// <summary>CARD-1717 D-1. Null when this request has no live gate, or the gate still allows the step. A system
    /// release without a gate never gets here: <see cref="AuthorityAsync"/> refuses it first.</summary>
    private static async Task<string?> LiveAuthorizationAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        if (request.LiveAuthorization is null) return null;
        return await request.LiveAuthorization(ct);
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
        if (request.CardDoneEndpointId is not null && !await evidence.CanDisposeCardDoneAsync(request, ct))
            return "card_done_authority_required";
        if (request.Purpose == WorktreeRemovalPurpose.SettledTask)
        {
            if (request.RetirementId is not Guid retirementId) return "retirement_receipt_required";
            var retirement = await evidence.ReadRetirementAsync(retirementId, ct);
            if (retirement?.CallerIdentity == "card-done" && request.CardDoneEndpointId is null)
                return "card_done_authority_required";
            if (retirement is null || retirement.Id != retirementId || !retirement.Active
                || retirement.TaskId != source.TaskId || retirement.SourceFullRef != source.SourceFullRef
                || retirement.SourceSha != request.ExpectedSourceSha
                || !LandingGit.PathsEqual(retirement.RepositoryPath, source.RepositoryPath)
                || !LandingGit.PathsEqual(retirement.WorktreePath, source.WorktreePath)
                || !LandingGit.PathsEqual(retirement.GitDirectory, request.GitDirectory)
                || !LandingGit.PathsEqual(retirement.CommonDirectory, common)
                || retirement.State is WorktreeRetirementState.Revoked)
                return "retirement_receipt_mismatch";
            // CARD-1717 B-B-1: a release the desktop legacy policy filed is authorized only by its live switch.
            // A request without that gate is refused at every entry (this check runs before each pass and the
            // branch delete), never read as permission.
            if (string.Equals(retirement.CallerIdentity, TaskWorktreeRetirementService.SystemCallerIdentity, StringComparison.Ordinal)
                && request.LiveAuthorization is null)
                return LiveAuthorizationRequired;
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
                var status = await git.StatusVerdictAsync(checkout, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
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
            || op.VerifiedSourceSha != Landed(request) || op.CleanupStartedAt is null
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
            new(op.RemoteName, op.DestinationFullRef, op.RemoteFingerprint), Landed(request),
            op.RecoveryRefPrefix + "/cleanup-observed", ct);
        return observed.Reason ?? (observed.ContainsSource ? null : "remote_no_longer_contains_source");
    }

    /// <summary>CARD-0688 D-6: a schema-3 land never moved the branch, so the branch/worktree identity
    /// (<see cref="WorktreeRemovalRequest.ExpectedSourceSha"/>) and the published commit differ.</summary>
    private static string Landed(WorktreeRemovalRequest request) => request.LandedSha ?? request.ExpectedSourceSha;

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
    private WorktreeIgnoredContent Classify(LandSourceSnapshot snapshot)
    {
        var content = ignored?.Classify(snapshot)
            ?? new([], [], [.. snapshot.IgnoredPaths.Order(StringComparer.Ordinal)]);
        // Git may collapse an ignored repository to dir/, hiding its private files from globs.
        // Filesystem evidence always overrides disposable/evidence ancestor rules at both readings.
        var repositories = WorktreeNestedRepositories.Find(snapshot.Coordinates.WorktreePath);
        return content with { Protected = [.. content.Protected.Concat(repositories)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] };
    }

    /// <summary>A stale list row for the tree just unregistered is not a checkout that still holds the branch.</summary>
    private static bool LiveCheckout(LandingRegistration row, WorktreeRemovalRequest request) =>
        !LandingGit.PathsEqual(row.Path, request.Source.WorktreePath) || RegistrationAdminPresent(request.GitDirectory);

    private static bool RegistrationAdminPresent(string gitDirectory)
    {
        try { _ = File.GetAttributes(Path.Combine(gitDirectory, "gitdir")); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    /// <summary>
    /// Null when the tree still matches the tracked index and the ignored names from the last inspection.
    /// A regular file in neither set is untracked_after_proof. Directory links are not flagged: the
    /// no-follow delete unlinks them without walking the target. A file symlink is the same. The
    /// worktree's own .git entry is administrative. An ls-files failure is source_changed.
    /// </summary>
    private async Task<string?> LateUntrackedAsync(string root, LandSourceSnapshot snapshot, CancellationToken ct)
    {
        LandingGitResult listed;
        try { listed = await git.RunAsync(root, ["ls-files", "-z"], ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        { return "source_changed"; }
        if (!listed.Succeeded || listed.WroteStandardError) return "source_changed";
        var tracked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            tracked.Add(name.Replace('\\', '/'));
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        if (!snapshot.IgnoredPaths.IsDefault)
            foreach (var path in snapshot.IgnoredPaths) ignored.Add(path.Replace('\\', '/'));
        try
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                    if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var stamp = WorktreeNoFollowDelete.TryStamp(entry);
                    if (stamp is null) return "source_changed";
                    if (stamp.Value.Kind == "dir") pending.Push(entry);
                    else if (stamp.Value.Kind == "file" && !tracked.Contains(relative) && !ignored.Contains(relative))
                        return "untracked_after_proof";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "source_changed"; }
        return null;
    }

    /// <summary>
    /// Null when every retained copy is present and matches, and every source that is still present matches.
    /// A missing retained copy is not success. An absent source was already removed.
    /// </summary>
    private static string? EvidenceProblem(string root, IReadOnlyList<RetainedWorktreeFile>? files)
    {
        if (files is null) return null;
        foreach (var file in files)
        {
            if (!RetainedHashAgrees(file.RetainedPath, file.Sha256))
            {
                var retained = WorktreeNoFollowDelete.RegularFileHash(file.RetainedPath);
                return retained is null || retained.Length == 0 ? "evidence_retained_missing" : "evidence_bytes_changed";
            }
            var source = Path.Combine(root, file.Relative.Replace('/', Path.DirectorySeparatorChar));
            if (!SourceHashAgrees(source, file.Sha256)) return "evidence_bytes_changed";
        }
        return null;
    }

    private static bool RetainedHashAgrees(string path, string expected)
    {
        var hash = WorktreeNoFollowDelete.RegularFileHash(path);
        if (hash is null || hash.Length == 0) return false;
        return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SourceHashAgrees(string path, string expected)
    {
        var hash = WorktreeNoFollowDelete.RegularFileHash(path);
        if (hash is null) return true;
        return hash.Length != 0 && hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct BoundContent(string? Reason, string? Entries, string? Evidence);

    /// <summary>One attempt's receipt. Discarded when <see cref="BindContentAsync"/> returns.</summary>
    private readonly record struct BoundReading(WorktreeNoFollowDelete.EntryStamp Stamp, string Sha256, string? GitBlob);

    /// <summary>
    /// One read of each content-bound file records the receipt SHA-256 and, for a tracked regular file,
    /// the git blob id. The second manifest reuses that SHA-256 when <see cref="WorktreeNoFollowDelete.TryStamp"/>
    /// still matches. A stamp change is hashed again. <see cref="CustodyOf"/> and the delete-time hash are
    /// separate reads and are not reused. The readings live only for this call.
    /// </summary>
    private async Task<BoundContent> BindContentAsync(WorktreeRemovalRequest request, string root, WorktreeIgnoredContent content,
        IReadOnlyList<RetainedWorktreeFile>? retained, CancellationToken ct)
    {
        if (EvidenceProblem(root, retained) is { } evidence) return new(evidence, null, null);
        var names = await TrackedNamesAsync(root, ct);
        if (names.Reason is not null) return new(names.Reason, null, null);
        var disposable = NameSet(content.Disposable);
        var evidenceNames = new HashSet<string>(StringComparer.Ordinal);
        if (retained is not null)
            foreach (var file in retained) evidenceNames.Add(file.Relative.Replace('\\', '/'));
        var readings = new Dictionary<string, BoundReading>(StringComparer.Ordinal);
        string before, after;
        try { before = EntryManifest(root, names.Names!, disposable, evidenceNames, readings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new("source_changed", null, null); }
        if (await TrackedBytesAsync(root, request, readings, ct) is { } tracked) return new(tracked, null, null);
        try { after = EntryManifest(root, names.Names!, disposable, evidenceNames, readings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new("source_changed", null, null); }
        if (!string.Equals(before, after, StringComparison.Ordinal)) return new("source_changed", null, null);
        return new(null, after, EvidenceManifest(retained));
    }

    private async Task<(string? Reason, HashSet<string>? Names)> TrackedNamesAsync(string root, CancellationToken ct)
    {
        LandingGitResult listed;
        try { listed = await git.RunAsync(root, ["ls-files", "-z"], ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        { return ("source_changed", null); }
        if (!listed.Succeeded || listed.WroteStandardError) return ("source_changed", null);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            names.Add(name.Replace('\\', '/'));
        return (null, names);
    }

    /// <summary>
    /// Null when the index is exactly the tree of the proven commit and every entry on disk has those
    /// bytes. CARD-1717 B-3: the index is read after the clean status, so an entry staged since then is
    /// not the pushed blob; the commit's own tree is the authority. A link entry is its target text, a
    /// regular file holding that text where links are not checked out. A gitlink holds no bytes here
    /// unless its directory is populated, and a populated one is a nested repository the census keeps.
    /// </summary>
    private async Task<string?> TrackedBytesAsync(string root, WorktreeRemovalRequest request,
        Dictionary<string, BoundReading> readings, CancellationToken ct)
    {
        var staged = await TreeEntriesAsync(root, ["ls-files", "--stage", "-z"], ct);
        if (staged is null) return "source_changed";
        var committed = await TreeEntriesAsync(request.Source.RepositoryPath,
            ["ls-tree", "-r", "-z", "--full-tree", request.ExpectedSourceSha], ct);
        if (committed is null) return "source_changed";
        if (staged.Count != committed.Count) return "tracked_bytes_changed";
        var filtered = new List<(string Relative, string Oid)>();
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        foreach (var (relative, entry) in staged)
        {
            if (!committed.TryGetValue(relative, out var expected) || expected != entry) return "tracked_bytes_changed";
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(rootFull, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return "source_changed";
            var stamp = WorktreeNoFollowDelete.TryStamp(full);
            if (entry.Mode == "160000")
            {
                if (stamp is null) continue;
                if (stamp.Value.Kind != "dir" || Directory.EnumerateFileSystemEntries(full).Any()) return "tracked_bytes_changed";
                continue;
            }
            if (stamp is null) return "tracked_bytes_changed";
            string? oid;
            if (entry.Mode == "120000" && stamp.Value.Kind == "link")
                oid = new FileInfo(full).LinkTarget is { } target
                    ? WorktreeNoFollowDelete.GitBlobOf(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(target))) : null;
            else if (stamp.Value.Kind != "file") return "tracked_bytes_changed";
            else
            {
                if (entry.Mode != "120000" && !OperatingSystem.IsWindows()
                    && File.GetUnixFileMode(full).HasFlag(UnixFileMode.UserExecute) != (entry.Mode == "100755"))
                    return "tracked_bytes_changed";
                // The receipt read already hashed these bytes. Re-read only when the stamp moved.
                oid = readings.TryGetValue(relative, out var bound) && bound.GitBlob is { Length: > 0 } && bound.Stamp == stamp.Value
                    ? bound.GitBlob
                    : WorktreeNoFollowDelete.GitBlobSha1(full);
                // CARD-1717 A-B-7: a checkout through core.autocrlf, an eol/text attribute or a clean filter holds
                // bytes that differ from the blob. Git's own clean form decides those below; the raw hashes of the
                // receipt still bind every byte that is later deleted.
                if (oid is { Length: > 0 } && !oid.Equals(entry.Oid, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add((relative, entry.Oid));
                    continue;
                }
            }
            if (oid is null || oid.Length == 0 || !oid.Equals(entry.Oid, StringComparison.OrdinalIgnoreCase))
                return "tracked_bytes_changed";
        }
        return filtered.Count == 0 ? null : await CleanFormDiffersAsync(root, filtered, ct);
    }

    /// <summary>
    /// CARD-1717 A-B-7. Null when <c>git hash-object</c>, run in the tree so each path's attributes and the
    /// repository's line-ending settings apply, gives every listed file its expected blob: the bytes are what
    /// <c>git diff</c> would call unchanged. Any other answer, or a failed command, is tracked_bytes_changed.
    /// </summary>
    private async Task<string?> CleanFormDiffersAsync(string root, List<(string Relative, string Oid)> files, CancellationToken ct)
    {
        const int batch = 100;
        for (var start = 0; start < files.Count; start += batch)
        {
            var chunk = files.GetRange(start, Math.Min(batch, files.Count - start));
            // safecrlf only warns about a later conversion; it does not change the hash.
            var arguments = new List<string> { "-c", "core.safecrlf=false", "hash-object", "--" };
            arguments.AddRange(chunk.Select(f => f.Relative));
            LandingGitResult hashed;
            try { hashed = await git.RunAsync(root, arguments, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            { return "tracked_bytes_changed"; }
            if (!hashed.Succeeded) return "tracked_bytes_changed";
            var oids = hashed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (oids.Length != chunk.Count) return "tracked_bytes_changed";
            for (var i = 0; i < chunk.Count; i++)
                if (!IsGitOid(oids[i]) || !oids[i].Equals(chunk[i].Oid, StringComparison.OrdinalIgnoreCase))
                    return "tracked_bytes_changed";
        }
        return null;
    }

    private readonly record struct TreeEntry(string Mode, string Oid);

    /// <summary>
    /// Parses <c>ls-files --stage -z</c> (mode oid stage) or <c>ls-tree -r -z</c> (mode type oid) rows by
    /// path. Null on a failed command, a conflict stage, an unknown mode or a malformed row.
    /// </summary>
    private async Task<Dictionary<string, TreeEntry>?> TreeEntriesAsync(string directory, string[] arguments, CancellationToken ct)
    {
        LandingGitResult listed;
        try { listed = await git.RunAsync(directory, arguments, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException) { return null; }
        if (!listed.Succeeded || listed.WroteStandardError) return null;
        var stage = arguments[0] == "ls-files";
        var entries = new Dictionary<string, TreeEntry>(StringComparer.Ordinal);
        foreach (var row in listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = row.IndexOf('\t');
            if (tab < 0) return null;
            var fields = row[..tab].Split(' ');
            if (fields.Length != 3) return null;
            var mode = fields[0];
            var oid = stage ? fields[1] : fields[2];
            if (stage ? fields[2] != "0" : fields[1] != (mode == "160000" ? "commit" : "blob")) return null;
            if (mode is not ("100644" or "100755" or "120000" or "160000") || !IsGitOid(oid)) return null;
            if (!entries.TryAdd(row[(tab + 1)..].Replace('\\', '/'), new(mode, oid.ToLowerInvariant()))) return null;
        }
        return entries;
    }

    private static bool IsGitOid(string value) => value.Length == 40
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static string EntryManifest(string root, HashSet<string> tracked, HashSet<string> disposable, HashSet<string> evidence,
        Dictionary<string, BoundReading> readings)
    {
        var entries = new List<WorktreeApprovedEntry>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var stamp = WorktreeNoFollowDelete.TryStamp(entry) ?? throw new IOException("manifest_entry_absent: " + relative);
                string? hash = null;
                if (stamp.Kind == "file" && BindsContent(relative, tracked, disposable, evidence))
                {
                    if (readings.TryGetValue(relative, out var prior) && prior.Stamp == stamp)
                        hash = prior.Sha256;
                    else
                    {
                        var digest = WorktreeNoFollowDelete.ReadReceipt(entry);
                        if (digest is null || digest.Value.Sha256.Length == 0)
                            throw new IOException("manifest_entry_unreadable: " + relative);
                        hash = digest.Value.Sha256;
                        if (!readings.ContainsKey(relative))
                            readings.Add(relative, new(stamp, hash,
                                tracked.Contains(relative) && digest.Value.GitBlob.Length > 0 ? digest.Value.GitBlob : null));
                    }
                }
                entries.Add(new(relative, stamp.Kind, hash));
                if (stamp.Kind == "dir") pending.Push(entry);
            }
        }
        entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return JsonSerializer.Serialize(entries);
    }

    private static bool BindsContent(string relative, HashSet<string> tracked, HashSet<string> disposable, HashSet<string> evidence) =>
        tracked.Contains(relative) || evidence.Contains(relative) || !disposable.Contains(relative);

    private static string EvidenceManifest(IReadOnlyList<RetainedWorktreeFile>? files)
    {
        var pins = new List<EvidencePin>();
        if (files is not null)
            foreach (var file in files)
                pins.Add(new(file.Relative.Replace('\\', '/'), file.RetainedPath, file.Sha256));
        pins.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        return JsonSerializer.Serialize(pins);
    }

    private static HashSet<string> NameSet(ImmutableArray<string> paths)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!paths.IsDefault)
            foreach (var path in paths) names.Add(path.Replace('\\', '/'));
        return names;
    }

    private void DeleteApproved(string aside, WorktreeSetAsideRecord record)
    {
        if (string.IsNullOrEmpty(record.EntryManifest)) throw new ApprovedTreeRejectedException("set_aside_unsealed");
        var listing = ApprovalListing(record.EntryManifest);
        var previous = RemovalApproval.Current.Value;
        RemovalApproval.Current.Value = listing;
        try { WorktreeNoFollowDelete.DeleteSettled(aside, preserveNestedRepositories: true, Renames); }
        finally { RemovalApproval.Current.Value = previous; }
    }

    private static RemovalApproval.Listing ApprovalListing(string manifest)
    {
        WorktreeApprovedEntry[] entries;
        try { entries = JsonSerializer.Deserialize<WorktreeApprovedEntry[]>(manifest) ?? []; }
        catch (JsonException) { throw new ApprovedTreeRejectedException("set_aside_unsealed"); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            names.Add(entry.Path);
            kinds[entry.Path] = entry.Kind;
            if (entry.Hash is not null) hashes[entry.Path] = entry.Hash;
        }
        return new RemovalApproval.Listing { Names = names, Kinds = kinds, Hashes = hashes };
    }

    /// <summary>Null when this directory may be moved back. Identity is the only restore authority.</summary>
    private static string? RestoreCustody(WorktreeSetAsideRecord record)
    {
        if (string.IsNullOrEmpty(record.DirectoryIdentity)) return "set_aside_unsealed";
        try
        {
            var identity = WorktreeNoFollowDelete.DirectoryIdentity(record.SetAsidePath);
            if (!string.Equals(identity, record.DirectoryIdentity, StringComparison.Ordinal))
                return "set_aside_identity_mismatch";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { return "set_aside_identity_mismatch"; }
        return null;
    }

    /// <summary>
    /// Null when the recorded tree may be deleted. Missing approved names are finished work, and an approved
    /// name that is now a link is only unlinked. A new or changed entry is refused. A nested repository, and
    /// nothing else unexpected, falls through so the delete's own census can preserve it.
    /// </summary>
    private static string? CustodyOf(WorktreeSetAsideRecord record)
    {
        if (string.IsNullOrEmpty(record.DirectoryIdentity) || string.IsNullOrEmpty(record.EntryManifest)
            || record.EvidenceManifest is null)
            return "set_aside_unsealed";
        string identity;
        try { identity = WorktreeNoFollowDelete.DirectoryIdentity(record.SetAsidePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { return "set_aside_identity_mismatch"; }
        if (!string.Equals(identity, record.DirectoryIdentity, StringComparison.Ordinal))
            return "set_aside_identity_mismatch";
        EvidencePin[] pins;
        WorktreeApprovedEntry[] entries;
        try
        {
            pins = JsonSerializer.Deserialize<EvidencePin[]>(record.EvidenceManifest) ?? [];
            entries = JsonSerializer.Deserialize<WorktreeApprovedEntry[]>(record.EntryManifest) ?? [];
        }
        catch (JsonException) { return "set_aside_unsealed"; }
        foreach (var pin in pins)
        {
            var retained = WorktreeNoFollowDelete.RegularFileHash(pin.RetainedPath);
            if (retained is null || retained.Length == 0) return "evidence_retained_missing";
            if (!retained.Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase)) return "evidence_bytes_changed";
            var source = Path.Combine(record.SetAsidePath, pin.Relative.Replace('/', Path.DirectorySeparatorChar));
            var sourceHash = WorktreeNoFollowDelete.RegularFileHash(source);
            if (sourceHash is null) continue;
            if (sourceHash.Length == 0 || !sourceHash.Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
                return "evidence_bytes_changed";
        }
        var approved = new Dictionary<string, WorktreeApprovedEntry>(StringComparer.Ordinal);
        foreach (var entry in entries) approved[entry.Path] = entry;
        var nested = WorktreeNestedRepositories.Find(record.SetAsidePath);
        try
        {
            var pending = new Stack<string>();
            pending.Push(record.SetAsidePath);
            while (pending.TryPop(out var directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var relative = Path.GetRelativePath(record.SetAsidePath, entry).Replace(Path.DirectorySeparatorChar, '/');
                    if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var stamp = WorktreeNoFollowDelete.TryStamp(entry);
                    if (stamp is null) continue;
                    if (!approved.TryGetValue(relative, out var expected))
                    {
                        if (!InsideNested(relative, nested)) return "set_aside_content_changed";
                        if (stamp.Value.Kind == "dir") pending.Push(entry);
                        continue;
                    }
                    // CARD-1017: an approved name that is now a link holds no worktree bytes; the delete unlinks it.
                    if (stamp.Value.Kind == "link") continue;
                    if (expected.Kind != stamp.Value.Kind) return "set_aside_content_changed";
                    if (expected.Hash is not null)
                    {
                        var hash = WorktreeNoFollowDelete.RegularFileHash(entry);
                        if (hash is null || hash.Length == 0 || !hash.Equals(expected.Hash, StringComparison.OrdinalIgnoreCase))
                            return "set_aside_content_changed";
                    }
                    if (stamp.Value.Kind == "dir") pending.Push(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "set_aside_content_changed"; }
        return null;
    }

    private static bool InsideNested(string relative, ImmutableArray<string> repositories)
    {
        foreach (var repository in repositories)
        {
            var prefix = repository.TrimEnd('/');
            if (relative == prefix || relative.StartsWith(prefix + "/", StringComparison.Ordinal)) return true;
            if (prefix.StartsWith(relative + "/", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // C448 V36: the refusal names its Win32 code; the residue is otherwise unchanged.
    private static string DeleteFailedDetail(string aside, Exception ex) =>
        "set-aside: " + aside + "; delete_error_" + WorktreeRenames.Win32Code(ex);

    private static bool IsAbsent(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }
}

internal sealed record WorktreeDirectoryPass(WorktreeRemoval Result, WorktreeGitOutcome? Outcome, long? FailureTimestamp);

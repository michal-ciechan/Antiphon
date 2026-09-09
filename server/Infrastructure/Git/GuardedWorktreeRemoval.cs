using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>One deletion path. Unknown authority, identity, content or I/O preserves residue.</summary>
public sealed class GuardedWorktreeRemoval(ILandingGit git, IRepositoryMutationLease leases,
    IWorktreeRemovalEvidence evidence)
{
    public async Task<WorktreeRemoval> RemoveAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        var source = request.Source;
        var directoryGone = false;
        var unregistered = false;
        var branchGone = false;
        WorktreeRemoval Refuse(string reason) => new(unregistered, directoryGone, branchGone, reason);
        try
        {
            var reason = await AuthorityAsync(request, ct);
            if (reason is not null) return Refuse(reason);
            var rows = await RegistrationsAsync(source.RepositoryPath, ct);
            var registration = rows.Where(r => LandingGit.PathsEqual(r.Path, source.WorktreePath)).ToList();
            // File.GetAttributes distinguishes absence from access/query errors.
            directoryGone = IsAbsent(source.WorktreePath);
            unregistered = registration.Count == 0;
            if (!directoryGone)
            {
                if (unregistered) return Refuse("unregistered_directory");
                var inspection = await git.InspectAsync(source, ct);
                if (!Matches(inspection, request)) return Refuse(inspection.Reason ?? "source_changed");
                // Non-forcing Git removal ALSO deletes ignored files. No patterns grant ownership.
                if (HasProtectedIgnored(inspection.Snapshot!)) return Refuse("ignored_content_preserved");
                reason = await AuthorityAsync(request, ct);
                if (reason is not null) return Refuse(reason);
                var final = await git.InspectAsync(source, ct);
                if (!Matches(final, request)) return Refuse(final.Reason ?? "source_changed");
                if (HasProtectedIgnored(final.Snapshot!)) return Refuse("ignored_content_preserved");
                // The final status/identity read can race a task-coordinate or receipt revision.
                // Revalidate durable authority immediately before deletion without reusing a tracked row.
                reason = await AuthorityAsync(request, ct, refreshRemote: false);
                if (reason is not null) return Refuse(reason);
                var removed = await git.RunAsync(source.RepositoryPath,
                    ["worktree", "remove", "--", source.WorktreePath], ct);
                if (!removed.Succeeded) return Refuse("worktree_remove_failed");
                directoryGone = IsAbsent(source.WorktreePath);
                rows = await RegistrationsAsync(source.RepositoryPath, ct);
                unregistered = !rows.Any(r => LandingGit.PathsEqual(r.Path, source.WorktreePath));
                if (!directoryGone || !unregistered) return Refuse("worktree_removal_incomplete");
            }
            else if (!unregistered || request.Purpose != WorktreeRemovalPurpose.Publication)
                return Refuse("missing_source_without_cleanup_receipt");

            // Refresh authority and all checkout registrations before the exact old-SHA delete.
            reason = await AuthorityAsync(request, ct);
            if (reason is not null) return Refuse(reason);
            if (!IsAbsent(source.WorktreePath)) return Refuse("source_recreated");
            rows = await RegistrationsAsync(source.RepositoryPath, ct);
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
            return new(unregistered, directoryGone, branchGone, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            return Refuse("cleanup_inspection_error");
        }
    }

    private async Task<string?> AuthorityAsync(WorktreeRemovalRequest request, CancellationToken ct, bool refreshRemote = true)
    {
        if (request.Purpose is not (WorktreeRemovalPurpose.Publication or WorktreeRemovalPurpose.LocalMerge))
            return "removal_purpose_unsupported";
        var source = request.Source;
        var common = await git.CommonDirectoryAsync(source.RepositoryPath, ct);
        if (!LandingGit.PathsEqual(common, request.CommonDirectory) || !leases.Owns(request.Lease, common))
            return "repository_lease_required";
        if (!LandingGit.IsOid(request.ExpectedSourceSha) || !LandingGit.IsOid(request.ExpectedTargetSha)
            || source.SourceFullRef == source.TargetFullRef) return "invalid_removal_identity";
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

    private static bool HasProtectedIgnored(LandSourceSnapshot snapshot) => snapshot.IgnoredPaths.Length != 0;

    private static bool IsAbsent(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }
}

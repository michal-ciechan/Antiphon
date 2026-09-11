using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Verification never borrows publication authority. Each file deletion needs fresh frozen evidence.</summary>
public sealed class GuardedVerificationRemoval(ILandingGit git, IRepositoryMutationLease leases, IWorktreeRemovalEvidence evidence)
{
    public async Task<WorktreeRemoval> RemoveAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        var directoryGone = false;
        var unregistered = false;
        var branchDeleted = false;
        WorktreeRemoval Refuse(string reason) => new(unregistered, directoryGone, branchDeleted, reason);
        try
        {
            var authority = await AuthorityAsync(request, ct);
            if (authority is null) return Refuse("verification_authority_missing_or_changed");
            var source = request.Source;
            directoryGone = IsAbsent(source.WorktreePath);
            if (directoryGone)
            {
                if (authority.RemovalStartedAt is null) return Refuse("verification_source_missing");
                unregistered = !(await git.RegistrationsAsync(source.RepositoryPath, ct)).Any(r => LandingGit.PathsEqual(r.Path, source.WorktreePath));
                if (!unregistered) return Refuse("missing_source_still_registered");
            }
            else
            {
            var files = await InspectAsync(request, authority, ct);
            if (files is null) return Refuse("verification_snapshot_not_restored");
            foreach (var output in authority.Restoration.Outputs)
            {
                var current = await AuthorityAsync(request, ct);
                if (current is null || !SameOutputs(authority, current)
                    || await InspectAsync(request, current, ct) is null)
                    return Refuse("verification_authority_or_content_changed");
                var path = Path.Combine(source.WorktreePath, output.RelativePath);
                if (!File.Exists(path)) continue; // Idempotent exact-file output cleanup.
                if (!await OutputMatchesAsync(source.WorktreePath, output, ct)) return Refuse("verification_output_changed");
                // InspectAsync has no destructive work. Re-read DB authority after its last Git/file read.
                current = await AuthorityAsync(request, ct);
                if (current is null || !SameOutputs(authority, current)) return Refuse("verification_authority_changed");
                File.Delete(path);
            }
            // Remove only now-empty ancestors of named outputs. Unknown empty directories were
            // refused by the inventory; no recursive filesystem removal is used here.
            foreach (var directory in OutputDirectories(source.WorktreePath, authority.Restoration.Outputs).OrderByDescending(p => p.Length))
            {
                if (!Directory.Exists(directory)) continue;
                var current = await AuthorityAsync(request, ct);
                if (current is null || !SameOutputs(authority, current) || await InspectAsync(request, current, ct) is null)
                    return Refuse("verification_authority_or_content_changed");
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, recursive: false);
            }
            var final = await AuthorityAsync(request, ct);
            if (final is null || !SameOutputs(authority, final) || await InspectAsync(request, final, ct) is null
                || await AuthorityAsync(request, ct) is null) return Refuse("verification_authority_or_content_changed");
            if (!await evidence.RecordVerificationRemovalStartAsync(request, ct)
                || await InspectAsync(request, final, ct) is null || await AuthorityAsync(request, ct) is null)
                return Refuse("verification_removal_start_unconfirmed");
            var remove = await git.RunAsync(source.RepositoryPath, ["worktree", "remove", "--", source.WorktreePath], ct);
            if (!remove.Succeeded) return Refuse("worktree_remove_failed");
            directoryGone = IsAbsent(source.WorktreePath);
            unregistered = !(await git.RegistrationsAsync(source.RepositoryPath, ct)).Any(r => LandingGit.PathsEqual(r.Path, source.WorktreePath));
            if (!directoryGone || !unregistered) return Refuse("worktree_removal_incomplete");
            }
            if (await AuthorityAsync(request, ct) is null) return Refuse("verification_authority_changed");
            if (!IsAbsent(source.WorktreePath)) return Refuse("source_recreated");
            if ((await git.RegistrationsAsync(source.RepositoryPath, ct)).Any(r => r.Branch == source.SourceFullRef))
                return Refuse("source_checked_out");
            if ((await git.RunAsync(source.RepositoryPath, ["symbolic-ref", "-q", source.SourceFullRef], ct)).ExitCode != 1)
                return Refuse("source_ref_symbolic_or_unresolved");
            if ((await git.RunAsync(source.RepositoryPath, ["show-ref", "--exists", source.SourceFullRef], ct)).ExitCode == 2)
                return new(unregistered, directoryGone, true, null);
            var deleted = await git.RunAsync(source.RepositoryPath,
                ["update-ref", "--no-deref", "-d", source.SourceFullRef, request.ExpectedSourceSha], ct);
            if (!deleted.Succeeded) return Refuse("branch_delete_failed");
            branchDeleted = (await git.RunAsync(source.RepositoryPath, ["show-ref", "--exists", source.SourceFullRef], ct)).ExitCode == 2;
            return new(unregistered, directoryGone, branchDeleted, branchDeleted ? null : "branch_deletion_unconfirmed");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        { return Refuse("verification_cleanup_inspection_failed"); }
    }

    private async Task<VerificationRemovalAuthority?> AuthorityAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        var common = await git.CommonDirectoryAsync(request.Source.RepositoryPath, ct);
        if (request.Purpose != WorktreeRemovalPurpose.Verification || !leases.Owns(request.Lease, common)
            || !LandingGit.PathsEqual(common, request.CommonDirectory)
            || !LandingGit.IsOid(request.ExpectedSourceSha)
            || await RepositoryChildJournal.HasUnfinishedAsync(common, git, ct)) return null;
        return await evidence.ReadVerificationAsync(request, ct);
    }

    private async Task<HashSet<string>?> InspectAsync(WorktreeRemovalRequest request, VerificationRemovalAuthority authority, CancellationToken ct)
    {
        var source = request.Source;
        var root = await git.CanonicalDirectoryAsync(source.WorktreePath, ct);
        if (!LandingGit.PathsEqual(root, source.WorktreePath) || !LandingGit.PathsEqual(await git.CommonDirectoryAsync(root, ct), request.CommonDirectory)) return null;
        var registrations = (await git.RegistrationsAsync(source.RepositoryPath, ct)).Where(r => LandingGit.PathsEqual(r.Path, root)).ToList();
        if (registrations.Count != 1 || registrations[0].Locked || registrations[0].Prunable || registrations[0].Branch != source.SourceFullRef) return null;
        var admin = await git.RunAsync(root, ["rev-parse", "--absolute-git-dir"], ct);
        if (!admin.Succeeded || !LandingGit.PathsEqual(await git.CanonicalDirectoryAsync(admin.Output.Trim(), ct), request.GitDirectory)
            || await git.HasActiveSequencerAsync(root, ct)) return null;
        var head = await git.RunAsync(root, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
        var symbolic = await git.RunAsync(root, ["symbolic-ref", "-q", "HEAD"], ct);
        var dirty = await git.RunAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=no", "--ignore-submodules=none"], ct);
        var tracked = await git.RunAsync(root, ["ls-files", "-z"], ct);
        if (!head.Succeeded || head.Output.Trim() != request.ExpectedSourceSha || !symbolic.Succeeded || symbolic.Output.Trim() != source.SourceFullRef
            || !dirty.Succeeded || dirty.Output.Length != 0 || !tracked.Succeeded) return null;
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var trackedPaths = tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.GetFullPath(Path.Combine(root, p))).ToHashSet(comparison);
        var allowed = new HashSet<string>(trackedPaths, comparison) { Path.Combine(root, ".git") };
        foreach (var output in authority.Restoration.Outputs)
        {
            var path = Path.GetFullPath(Path.Combine(root, output.RelativePath));
            if (trackedPaths.Contains(path) || !allowed.Add(path) || !await OutputMatchesAsync(root, output, ct)) return null;
        }
        var allowedDirectories = new HashSet<string>(comparison);
        foreach (var file in allowed)
            for (var parent = Path.GetDirectoryName(file); parent is not null && !LandingGit.PathsEqual(parent, root); parent = Path.GetDirectoryName(parent))
                allowedDirectories.Add(parent);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!allowedDirectories.Contains(entry)) return null;
                    pending.Push(entry);
                }
                else if (!allowed.Contains(entry)) return null;
            }
        return trackedPaths;
    }

    private async Task<bool> OutputMatchesAsync(string root, VerificationOutput output, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(output.RelativePath) || Path.IsPathRooted(output.RelativePath)
            || output.RelativePath.Contains(':') || output.RelativePath.Split('/', '\\').Any(p => p is "." or "..")) return false;
        var path = Path.GetFullPath(Path.Combine(root, output.RelativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        for (var cursor = path; !LandingGit.PathsEqual(cursor, root); cursor = Path.GetDirectoryName(cursor)!)
        {
            try { if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        if (Directory.Exists(path)) return false;
        return !File.Exists(path) || Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))) == output.Sha256;
    }

    private IEnumerable<string> OutputDirectories(string root, VerificationOutput[] outputs)
    {
        var result = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var output in outputs)
            for (var parent = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, output.RelativePath)));
                parent is not null && !LandingGit.PathsEqual(parent, root); parent = Path.GetDirectoryName(parent)) result.Add(parent);
        return result;
    }

    private bool SameOutputs(VerificationRemovalAuthority left, VerificationRemovalAuthority right) =>
        left.Seal == right.Seal with { Executions = left.Seal.Executions }
        && left.Seal.Executions.SequenceEqual(right.Seal.Executions)
        && left.Restoration == right.Restoration with { Outputs = left.Restoration.Outputs }
        && left.Restoration.Outputs.SequenceEqual(right.Restoration.Outputs);

    private bool IsAbsent(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }
}
